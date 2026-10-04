using SerialPortLib;
using System;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Denon.Core.Command
{
    /// <summary>
    /// RS-232 port settings. The Denon default is 9600 baud, 8 data bits, no parity, 1 stop bit.
    /// </summary>
    public class SerialSettings
    {
        /// <summary>"COM3" on Windows, "/dev/ttyUSB0" (USB adapter) or "/dev/ttyS0" on Linux.</summary>
        public string PortName { get; set; }
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public Parity Parity { get; set; } = Parity.None;
        public StopBits StopBits { get; set; } = StopBits.One;
    }


    /// <summary>
    /// Serial (RS-232) connection to the receiver, using SerialPortLib (the same library as
    /// the Denon.Serial.Test tool). The port stays open between commands, and SerialPortLib
    /// reopens it on its own if it drops (for example, a USB adapter unplugged and replugged).
    /// </summary>
    public sealed class SerialConnection : IDenonConnection, IDisposable
    {
        // After the port fails to open, fail immediately for this long instead of trying again,
        // so queued requests don't each wait on the same failure (same as the telnet connection).
        private const int RetryAfterFailureMs = 5000;

        // Text the receiver sends while no command is waiting is kept until the next command
        // (which discards it); cap it so an idle API doesn't hold an ever-growing buffer.
        private const int MaxBufferedChars = 64 * 1024;

        // The Denon protocol is plain ASCII; Latin-1 maps every byte to one character, so
        // nothing is lost if the receiver ever sends a byte above 127.
        private static readonly Encoding Wire = Encoding.Latin1;

        private readonly SerialSettings _settings;
        private readonly SerialPortInput _port = new SerialPortInput();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly StringBuilder _received = new StringBuilder();
        private readonly object _receivedLock = new object();

        private DateTime _lastFailureUtc = DateTime.MinValue;
        private string _lastError;
        private bool _disposed;

        public SerialConnection(SerialSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.PortName)) throw new ArgumentException("DeviceConfig:Serial:PortName is required for a serial connection.", nameof(settings));

            _port.SetPort(settings.PortName, settings.BaudRate, settings.StopBits, settings.Parity, ToDataBits(settings.DataBits));

            _port.MessageReceived += (sender, args) =>
            {
                lock (_receivedLock)
                {
                    _received.Append(Wire.GetString(args.Data));
                    if (_received.Length > MaxBufferedChars) _received.Remove(0, _received.Length - MaxBufferedChars);
                }
            };

            _port.ConnectionStatusChanged += (sender, args) =>
                System.Diagnostics.Debug.WriteLine($"Serial port {settings.PortName} {(args.Connected ? "connected" : "disconnected")}");
        }

        public string Description => $"serial port {_settings.PortName}";

        public async Task<string> Send(string command, string terminatorMatch, int timeout, string responseFilter = null)
        {
            Regex terminator = ReplyReader.Terminator(terminatorMatch);
            Regex filter = ReplyReader.Filter(responseFilter);

            await _lock.WaitAsync();

            try
            {
                EnsureConnected();

                // Throw away anything already received: status messages the receiver sent on
                // its own since the last command, or trailing lines of the previous reply.
                string stale = TakeReceived();
                if (stale.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Discarded unread serial data before [{ReplyReader.Escape(command)}]: [{ReplyReader.Escape(stale)}]");
                }

                if (!_port.SendMessage(Wire.GetBytes(command)))
                {
                    MarkFailed("writing to the port failed");
                    throw new DeviceUnreachableException($"Receiver on {Description} is not reachable ({_lastError}).");
                }

                (string response, _) = await ReplyReader.ReadAsync(ReadChunk, terminator, filter, timeout);
                return response;
            }
            finally
            {
                _lock.Release();
            }
        }

        private void EnsureConnected()
        {
            if (_port.IsConnected) return;

            if (DateTime.UtcNow - _lastFailureUtc < TimeSpan.FromMilliseconds(RetryAfterFailureMs))
            {
                throw new DeviceUnreachableException($"Receiver on {Description} is not reachable ({_lastError}).");
            }

            bool connected;
            try
            {
                connected = _port.Connect();
            }
            catch (Exception e)
            {
                MarkFailed(e.Message);
                throw new DeviceUnreachableException($"Receiver on {Description} is not reachable ({_lastError}).", e);
            }

            if (!connected)
            {
                MarkFailed("the port could not be opened; check the port name and that nothing else is using it");
                throw new DeviceUnreachableException($"Receiver on {Description} is not reachable ({_lastError}).");
            }

            _lastFailureUtc = DateTime.MinValue;
            _lastError = null;
        }

        private void MarkFailed(string reason)
        {
            _lastFailureUtc = DateTime.UtcNow;
            _lastError = reason;
        }

        private string TakeReceived()
        {
            lock (_receivedLock)
            {
                string text = _received.ToString();
                _received.Clear();
                return text;
            }
        }

        // For ReplyReader: whatever has arrived since the last call, waiting briefly if nothing has.
        private async Task<string> ReadChunk()
        {
            string text = TakeReceived();
            if (text.Length == 0) await Task.Delay(5);
            return text;
        }

        private static DataBits ToDataBits(int bits) => bits switch
        {
            5 => SerialPortLib.DataBits.Five,
            6 => SerialPortLib.DataBits.Six,
            7 => SerialPortLib.DataBits.Seven,
            8 => SerialPortLib.DataBits.Eight,
            _ => throw new ArgumentException($"DeviceConfig:Serial:DataBits must be 5, 6, 7 or 8 (was {bits}).")
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { if (_port.IsConnected) _port.Disconnect(); } catch { /* shutting down */ }
            _lock.Dispose();
        }
    }
}
