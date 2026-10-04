using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;



namespace Denon.Core.Model
{
    public class Control
    {
        public enum SystemStateType
        {
            Unknown = 0,
            Alive,
            Error
        }

        public enum PowerStateType
        {
            Unknown = 0,
            On,
            Off,
            Standby
        }

        public enum MuteStateType
        {
            Unknown = 0,
            On,
            Off
        }

        public enum SourceStateType
        {
            Unknown = 0,
            Ok
        }

        public enum VolumeStateType
        {
            Unknown = 0,
            On,
        }


        public class ResponseItem
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }


        public class CommandResponse
        {
            public DateTime LastUpdate { get; set; }
            public string Command { get; set; }

            public string Response { get; set; }

            public List<ResponseItem> ResponseItems { get; set; }

            public CommandResponse()
            {
                ResponseItems = new List<ResponseItem>();
            }


            public void SetResponse(CommandItem deviceCommand, string response)
            {
                this.Response = response;


                List<Control.ResponseItem> ri = deviceCommand.ResponseCaptureType switch
                {
                    CaptureType.CaptureArray => BuildResponseCaptureArrayItems(deviceCommand, response),
                    CaptureType.SingleCapture => BuildResponseSingleCaptureItems(deviceCommand, response),
                    _ => BuildResponseNameGroupItems(deviceCommand, response),
                };

                // Perhaps use copyto
                this.ResponseItems = ri;
            }


            private List<Control.ResponseItem> BuildResponseCaptureArrayItems(CommandItem deviceCommand, string response)
            {
                List<Control.ResponseItem> ri = new List<Control.ResponseItem>();

                Regex responseRegEx = new Regex(deviceCommand.ResponseLayout, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ExplicitCapture);
                Match rm;
                foreach (string element in response.Split("\x0D"))
                {
                    rm = responseRegEx.Match(element);
                    if (!String.IsNullOrEmpty(rm.Groups["name"].Value))
                    {
                        ri.Add(new Control.ResponseItem()
                        {
                            Name = rm.Groups["name"].Value,
                            Value = rm.Groups["value"].Value?.Trim()
                        });

                    }
                }

                return ri;

            }


            private List<Control.ResponseItem> BuildResponseNameGroupItems(CommandItem deviceCommand, string response)
            {
                List<Control.ResponseItem> ri = new List<Control.ResponseItem>();

                Regex responseRegEx = new Regex(deviceCommand.ResponseLayout, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ExplicitCapture);
                Match rm;
                foreach (string element in response.Split("\x0D"))
                {
                    rm = responseRegEx.Match(element);
                    foreach (Group g in rm.Groups)
                    {
                        if (deviceCommand.ResponseElements.Exists(e => e.Name.ToLower() == g.Name.ToLower() && !string.IsNullOrEmpty(g.Value)))
                        {
                            ri.Add(new Control.ResponseItem() { Name = g.Name, Value = g.Value?.Trim() });
                            break;
                        }
                    }
                }

                return ri;

            }


            private List<Control.ResponseItem> BuildResponseSingleCaptureItems(CommandItem deviceCommand, string response)
            {
                List<Control.ResponseItem> ri = new List<Control.ResponseItem>();

                Regex responseRegEx = new Regex(deviceCommand.ResponseLayout, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                Match rm = responseRegEx.Match(response);
                foreach (Group g in rm.Groups)
                {
                    if (deviceCommand.ResponseElements.Exists(e => e.Name.ToLower() == g.Name.ToLower() && !string.IsNullOrEmpty(g.Value)))
                    {
                        if (g.Name != "0") ri.Add(new Control.ResponseItem() { Name = g.Name, Value = g.Value?.Trim() });
                    }
                }

                return ri;

            }

        }


        // Source list from the device schema: every source with its labels and enabled flag.
        // ResponseItems carries the same list as code/label pairs for older callers.
        public class SourceListResponse : CommandResponse
        {
            public List<SourceItem> Sources { get; set; }
        }


        public class ModelStatus : CommandResponse
        {
            public string Model { get; set; }

        }


        public class SystemStatus : CommandResponse
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public SystemStateType SystemState { get; set; }

        }


        public class PowerStatus : CommandResponse
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public PowerStateType PowerState { get; set; }

            public decimal Level { get; set; }
            public string Source { get; set; }
        }


        public class VolumeStatus : CommandResponse
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public VolumeStateType VolumeState { get; set; }

            public decimal Level { get; set; }
            public decimal MaxLevel { get; set; }
        }


        public class MuteStatus : CommandResponse
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public MuteStateType MuteState { get; set; }
        }


        public class SourceStatus : CommandResponse
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public SourceStateType SourceState { get; set; }
            public string Source { get; set; }
        }


    }
}
