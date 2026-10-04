using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Denon.Core.Command
{
    /// <summary>
    /// Network connection to the receiver's control port (TCP 23, which Denon calls "telnet").
    /// The protocol is plain CR-terminated ASCII with no login and no telnet option negotiation,
    /// so this talks to the socket directly with .NET's TcpClient rather than a telnet library.
    ///
    /// One command at a time. The connection stays open between commands, is reopened if the
    /// receiver has dropped it, and is closed after a period without commands.
    /// </summary>
    public sealed class TcpConnection : IDenonConnection, IDisposable
    {
        // Time allowed to open the connection. Separate from the per-command reply timeout,
        // which can be as short as 100 ms and is too tight for a connect.
        private const int ConnectTimeoutMs = 2000;

        // After a failed connect, fail immediately for this long instead of trying again, so
        // queued requests don't each wait out their own connect timeout.
        private const int RetryAfterFailureMs = 5000;

        // The Denon protocol is plain ASCII; Latin-1 maps every byte to one character.
        private static readonly Encoding Wire = Encoding.Latin1;

        private readonly string _host;
        private readonly int _port;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly System.Timers.Timer _idleTimer;

        private TcpClient _client;
        private NetworkStream _stream;
        private DateTime _lastConnectFailureUtc = DateTime.MinValue;
        private string _lastConnectError;
        private bool _disposed;

        /// <param name="host">Receiver IP address or host name.</param>
        /// <param name="port">Control port (the receiver uses 23).</param>
        /// <param name="idleTimeoutMs">Close the connection after this long without commands.</param>
        public TcpConnection(string host, int port = 23, long idleTimeoutMs = 300000)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A receiver host is required for a telnet connection.", nameof(host));

            _host = host;
            _port = port;

            _idleTimer = new System.Timers.Timer(Math.Max(1, idleTimeoutMs)) { AutoReset = false };
            _idleTimer.Elapsed += (sender, args) => CloseIdleConnection();
        }

        public string Description => $"telnet {_host}:{_port}";

        /// <summary>
        /// Opens the connection now rather than on the first command, so a wrong address is
        /// reported straight away.
        /// </summary>
        /// <exception cref="DeviceUnreachableException">The receiver couldn't be reached.</exception>
        public async Task ConnectAsync()
        {
            await _lock.WaitAsync();
            try
            {
                await EnsureConnectedAsync();
            }
            finally
            {
                RestartIdleTimer();
                _lock.Release();
            }
        }

        public async Task<string> Send(string command, string terminatorMatch, int timeout, string responseFilter = null)
        {
            Regex terminator = ReplyReader.Terminator(terminatorMatch);
            Regex filter = ReplyReader.Filter(responseFilter);

            await _lock.WaitAsync();

            try
            {
                // One retry, and only when a write fails on a connection we reused (it may have
                // died since the last command). A fresh connection that fails is reported at once.
                for (int attempt = 1; ; attempt++)
                {
                    bool reusedConnection = false;

                    try
                    {
                        reusedConnection = await EnsureConnectedAsync();

                        // Throw away anything already received: status messages the receiver sent
                        // on its own since the last command, or trailing lines of the previous reply.
                        string stale = ReadAvailable();
                        if (stale.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Discarded unread telnet data before [{ReplyReader.Escape(command)}]: [{ReplyReader.Escape(stale)}]");
                        }

                        byte[] bytes = Wire.GetBytes(command);
                        _stream.Write(bytes, 0, bytes.Length);

                        (string response, bool receivedAnything) = await ReplyReader.ReadAsync(ReadChunk, terminator, filter, timeout);

                        if (!receivedAnything && reusedConnection)
                        {
                            // No reply on a reused connection usually means it's half-open (the
                            // receiver went away without closing it). Drop it so the next command
                            // reconnects. Don't resend: the command may have arrived, and repeating
                            // a relative command like MVUP would double it.
                            System.Diagnostics.Debug.WriteLine("No response on reused telnet connection; dropping it");
                            Close();
                        }

                        return response;
                    }
                    catch (DeviceUnreachableException)
                    {
                        throw;
                    }
                    catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                    {
                        Close();

                        if (reusedConnection && attempt == 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"Send on reused telnet connection failed with [{e.Message}]; retrying on a new connection");
                            continue;
                        }

                        throw new DeviceUnreachableException($"Receiver at {_host} is not reachable ({e.Message}).", e);
                    }
                }
            }
            finally
            {
                RestartIdleTimer();
                _lock.Release();
            }
        }

        // Returns true if an open connection was reused, false if a new one was opened.
        // Must be called while holding _lock.
        private async Task<bool> EnsureConnectedAsync()
        {
            if (_client != null && !IsAlive())
            {
                System.Diagnostics.Debug.WriteLine("Telnet connection is no longer alive; reconnecting");
                Close();
            }

            if (_client != null) return true;

            if (DateTime.UtcNow - _lastConnectFailureUtc < TimeSpan.FromMilliseconds(RetryAfterFailureMs))
            {
                throw new DeviceUnreachableException($"Receiver at {_host} is not reachable ({_lastConnectError}).");
            }

            TcpClient client = new TcpClient { NoDelay = true };
            string error = null;
            Exception inner = null;

            try
            {
                using CancellationTokenSource timeout = new CancellationTokenSource(ConnectTimeoutMs);
                await client.ConnectAsync(_host, _port, timeout.Token);
            }
            catch (OperationCanceledException e)
            {
                error = $"Connection to {_host}:{_port} timed out after {ConnectTimeoutMs} ms.";
                inner = e;
            }
            catch (SocketException e)
            {
                error = $"Connection to {_host}:{_port} failed: {e.Message}";
                inner = e;
            }

            if (error != null)
            {
                client.Dispose();
                _lastConnectFailureUtc = DateTime.UtcNow;
                _lastConnectError = error;
                throw new DeviceUnreachableException($"Receiver at {_host} is not reachable ({error}).", inner);
            }

            _client = client;
            _stream = client.GetStream();
            _lastConnectFailureUtc = DateTime.MinValue;
            _lastConnectError = null;
            return false;
        }

        // Whether the receiver still has the connection open. A socket that polls readable with
        // nothing to read has received the receiver's FIN; Socket.Connected wouldn't show that
        // until a read or write failed.
        private bool IsAlive()
        {
            try
            {
                Socket socket = _client?.Client;
                if (socket == null || !socket.Connected) return false;
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (Exception e) when (e is ObjectDisposedException || e is SocketException)
            {
                return false;
            }
        }

        // Everything received so far, without waiting.
        private string ReadAvailable()
        {
            if (_client == null || _stream == null) return "";

            StringBuilder text = new StringBuilder();
            byte[] buffer = new byte[1024];

            while (_client.Available > 0)
            {
                int count = _stream.Read(buffer, 0, Math.Min(buffer.Length, _client.Available));
                if (count <= 0) break;
                text.Append(Wire.GetString(WithoutTelnetCommands(buffer, count)));
            }

            return text.ToString();
        }

        // For ReplyReader: whatever has arrived since the last call, waiting briefly if nothing has.
        private async Task<string> ReadChunk()
        {
            string text = ReadAvailable();
            if (text.Length == 0) await Task.Delay(2);
            return text;
        }

        // The receiver doesn't use telnet option negotiation, but drop any IAC sequences
        // (0xFF ...) in case something on the path sends them, so they can't show up in a reply.
        private static byte[] WithoutTelnetCommands(byte[] buffer, int count)
        {
            if (Array.IndexOf(buffer, (byte)0xFF, 0, count) < 0) return buffer.AsSpan(0, count).ToArray();

            List<byte> data = new List<byte>(count);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] != 0xFF) { data.Add(buffer[i]); continue; }
                if (i + 1 >= count) break;

                byte verb = buffer[i + 1];
                if (verb == 0xFF) { data.Add(0xFF); i++; }                      // escaped 0xFF
                else if (verb >= 251 && verb <= 254) i += 2;                     // WILL/WONT/DO/DONT <option>
                else i++;                                                        // other two-byte command
            }
            return data.ToArray();
        }

        private void RestartIdleTimer()
        {
            if (_disposed) return;
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        private void CloseIdleConnection()
        {
            if (_disposed) return;

            try
            {
                _lock.Wait();
                try
                {
                    if (_client != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Closing idle telnet connection");
                        Close();
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Disposed while the timer fired.
            }
        }

        // Must be called while holding _lock (or from Dispose).
        private void Close()
        {
            try { _stream?.Dispose(); } catch { /* already gone */ }
            try { _client?.Dispose(); } catch { /* already gone */ }
            _stream = null;
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _idleTimer.Stop();
            _idleTimer.Dispose();
            Close();
            _lock.Dispose();
        }
    }
}
