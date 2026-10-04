using System;
using System.Threading.Tasks;

namespace Denon.Core.Command
{
    /// <summary>
    /// A connection to the receiver, over telnet or RS-232. The API sends every command through
    /// this, so the connection type is purely a configuration choice.
    /// </summary>
    public interface IDenonConnection
    {
        /// <summary>
        /// Sends a command and returns its reply.
        /// </summary>
        /// <param name="command">The command, including its trailing CR.</param>
        /// <param name="terminatorMatch">Regex; reading stops as soon as the reply matches it.</param>
        /// <param name="timeout">Longest time to wait for the reply, in milliseconds.</param>
        /// <param name="responseFilter">
        /// Optional regex. When set, only reply lines matching it are counted toward
        /// <paramref name="terminatorMatch"/> and returned; other lines (status messages the
        /// receiver sends on its own) are ignored.
        /// </param>
        /// <exception cref="DeviceUnreachableException">The receiver couldn't be reached.</exception>
        Task<string> Send(string command, string terminatorMatch, int timeout, string responseFilter = null);

        /// <summary>For logs and messages, e.g. "telnet 192.168.1.100:23" or "serial port COM3".</summary>
        string Description { get; }
    }


    /// <summary>
    /// The receiver couldn't be reached (connection refused or timed out, serial port missing,
    /// or a recent attempt failed and the retry window hasn't passed yet).
    /// </summary>
    public class DeviceUnreachableException : Exception
    {
        public DeviceUnreachableException(string message, Exception inner = null) : base(message, inner) { }
    }
}
