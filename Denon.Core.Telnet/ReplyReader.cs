using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Denon.Core.Command
{
    /// <summary>
    /// Reads a command's reply from the receiver. Shared by the telnet and serial connections,
    /// which differ only in how they fetch the next chunk of received text.
    /// </summary>
    internal static class ReplyReader
    {
        internal const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ExplicitCapture;

        internal static Regex Terminator(string terminatorMatch) => new Regex(terminatorMatch, Options);

        internal static Regex Filter(string responseFilter) => string.IsNullOrEmpty(responseFilter) ? null : new Regex(responseFilter, Options);

        // Reads until the terminator matches or the timeout passes. The Denon protocol ends
        // every message with CR, so data is split into lines; with a filter, lines that don't
        // match it (status messages the receiver sends on its own) are dropped and don't count
        // toward the terminator. Returns the kept lines (each ending in CR) and whether any data
        // at all arrived.
        //
        // readChunk returns whatever text has arrived since the last call ("" if none) and
        // should wait briefly when there's nothing, so this loop doesn't spin.
        internal static async Task<(string Response, bool ReceivedAnything)> ReadAsync(Func<Task<string>> readChunk, Regex terminator, Regex filter, int timeout)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeout);
            StringBuilder kept = new StringBuilder();
            string pending = "";
            bool receivedAnything = false;

            void HandleLine(string line)
            {
                line = line.Trim('\n');
                if (line.Length == 0) return;

                if (filter is null || filter.IsMatch(line))
                {
                    kept.Append(line).Append('\r');
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Ignored unrelated line [{Escape(line)}]");
                }
            }

            while (true)
            {
                string chunk = await readChunk();

                if (!String.IsNullOrEmpty(chunk))
                {
                    receivedAnything = true;
                    pending += chunk;

                    int idx;
                    while ((idx = pending.IndexOf('\r')) >= 0)
                    {
                        HandleLine(pending.Substring(0, idx));
                        pending = pending.Substring(idx + 1);
                    }

                    if (terminator.IsMatch(kept.ToString())) break;
                }

                if (DateTime.Now >= deadline) break;
            }

            // A partial line still waiting for its CR when time ran out.
            if (pending.Length > 0) HandleLine(pending);

            return (kept.ToString(), receivedAnything);
        }

        internal static string Escape(string s) => System.Web.HttpUtility.JavaScriptStringEncode(s);
    }
}
