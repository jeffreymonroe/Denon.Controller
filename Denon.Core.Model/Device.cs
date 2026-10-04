using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Denon.Core.Model
{
    public enum CaptureType
    {
        NamedGroups = 0,
        CaptureArray,
        SingleCapture
    }

    public class Device
    {
        public string Model { get; set; }
        public string Manufacturer { get; set; }
        public ConfigurationItem Configuration { get; set; }
        public List<CommandItem> Commands { get; set; }

        public Device()
        {
            Commands = new List<CommandItem>();
        }
    }

    public class ConfigurationItem
    {
        public List<ZoneItem> Zones { get; set; }

        // Input sources, from the schema's "sources" list. Empty if the schema has none.
        public List<SourceItem> Sources { get; set; }

        // Filled in by the API's GetConfiguration (not part of the schema's configuration):
        // the receiver's model as it reports it (or the schema's model if it doesn't answer),
        // and the API's version.
        public string Model { get; set; }
        public string ApiVersion { get; set; }

        public ConfigurationItem()
        {
            Zones = new List<ZoneItem>();
            Sources = new List<SourceItem>();
        }

    }

    public class SourceItem
    {
        // The receiver's code for the input, used in commands (SICD, Z2CD, ...).
        public string Id { get; set; }

        // The input's standard label.
        public string Default { get; set; }

        // Optional custom label (e.g. "Raspotify" for CD). Empty means use Default.
        public string Name { get; set; }

        // Whether the web remote offers this source in its source list. The API still returns
        // and accepts every source; this only controls what the remote displays.
        public bool Enabled { get; set; } = true;

        // The label to show: Name if set, otherwise Default (or Id if neither is set).
        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
            : !string.IsNullOrWhiteSpace(Default) ? Default.Trim()
            : Id;
    }

    public class ZoneItem
    {
        public string Id { get; set; }
        public string Default { get; set; }
        public string Name { get; set; }

        // Highest volume the API allows for this zone. Filled in by the API from its
        // Safety settings when it returns the configuration; not part of the device schema.
        public decimal? MaxVolume { get; set; }
    }

    public class CommandItem
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string ResponseLayout { get; set; }
        public int Timeout { get; set; }
        public string Terminator { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CaptureType ResponseCaptureType { get; set; }
        public List<ResponseElementItem> ResponseElements { get; set; }

        public CommandItem()
        {
            ResponseElements = new List<ResponseElementItem>();
        }


        public void FormatCommand(params string[] args)
        {
            if (args != null)
            {
                // Arguments must be plain printable ASCII. A CR or LF would end the command
                // early and let the rest of the value run as a second command.
                foreach (string arg in args)
                {
                    if (arg is null) continue;
                    foreach (char c in arg)
                    {
                        if (c < 0x20 || c > 0x7E)
                        {
                            throw new ArgumentException($"Command argument contains a control or non-ASCII character (0x{(int)c:X2}).", nameof(args));
                        }
                    }
                }

                this.Command = String.Format(this.Command, args);
            }

        }

        public void FormatResponseLayout(params string[] args)
        {
            if (args != null)
            {
                // Arguments are literal text inside a regex, so escape them.
                string[] escaped = Array.ConvertAll(args, a => a is null ? null : Regex.Escape(a));
                this.ResponseLayout = String.Format(this.ResponseLayout, escaped);
            }
        }
    }

    public class ResponseElementItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

}
