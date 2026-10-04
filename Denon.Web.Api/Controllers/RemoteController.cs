using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Denon.Core.Model;
using Denon.Core.Command;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.Configuration;
using Denon.Core.Extensions;
using System.ComponentModel.DataAnnotations;

namespace Denon.Web.Api.Controllers
{
    [EnableCors("CorsPolicy")]
    [DeviceErrorResult]
    [Route("api/[controller]")]
    [ApiController]
    public class RemoteController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly IDenonDevice _device;
        private readonly IDenonConnection _connection;
        private readonly IConfiguration _configuration;
        private readonly int _timeoutRatio;

        // This API's version, e.g. "1.1.0+96afac9..." (from Directory.Build.props and the git commit).
        private static readonly string ApiVersion =
            typeof(RemoteController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(RemoteController).Assembly.GetName().Version?.ToString();

        // The model the receiver reported to SYMO, kept once read so page loads don't query it every time.
        private static string _reportedModel;


        public RemoteController(IConfiguration configuration, IDenonConnection connection, IDenonDevice device, ILogger<RemoteController> logger)
        {
            _logger = logger;
            _device = device;
            _connection = connection;
            _configuration = configuration;
            _timeoutRatio = int.Parse(_configuration["DeviceConfig:TimeoutRatio"]);
        }


        // ---------------------------------------------------------------------------------
        // Command helpers
        // ---------------------------------------------------------------------------------

        // Runs a command from the device schema and returns the parsed reply: formats args into
        // the command and its response layout, sends it, and fills in Command, Response and
        // ResponseItems. The typed helpers below add the fields specific to each status type.
        private async Task<T> RunCommand<T>(string commandName, params string[] args) where T : Control.CommandResponse, new()
        {
            CommandItem deviceCommand = _device.GetDeviceCommand(commandName);

            if (args.Length > 0)
            {
                deviceCommand.FormatCommand(args);
                deviceCommand.FormatResponseLayout(args);
            }

            T result = new T
            {
                LastUpdate = DateTime.Now,
                Command = deviceCommand.Command
            };

            string telnetResponse = await SendDeviceCommand(deviceCommand);
            result.SetResponse(deviceCommand, telnetResponse);

            return result;
        }

        private async Task<Control.PowerStatus> RunSystemPowerCommand(string commandName)
        {
            var result = await RunCommand<Control.PowerStatus>(commandName);
            result.PowerState = result.ResponseItems.GetResponseItemValue("pw_status").GetEnumFromString<Control.PowerStateType>();
            return result;
        }

        private async Task<Control.PowerStatus> RunZonePowerCommand(string commandName, string zoneId)
        {
            var result = await RunCommand<Control.PowerStatus>(commandName, zoneId);
            result.PowerState = result.ResponseItems.GetResponseItemValue("zone_status").GetEnumFromString<Control.PowerStateType>();
            result.Level = VolumeLevelConvert(result.ResponseItems.GetResponseItemValue("volume"));
            result.Source = result.ResponseItems.GetResponseItemValue("source");
            return result;
        }

        // Parses the level from a volume-related reply.
        private async Task<Control.VolumeStatus> RunVolumeCommand(string commandName, params string[] args)
        {
            var result = await RunCommand<Control.VolumeStatus>(commandName, args);
            result.Level = VolumeLevelConvert(result.ResponseItems.GetResponseItemValue("volume"));
            result.MaxLevel = VolumeLevelConvert(result.ResponseItems.GetResponseItemValue("volume_max"));
            if (result.Level > -1) result.VolumeState = Control.VolumeStateType.On;
            return result;
        }

        private async Task<Control.MuteStatus> RunMuteCommand(string commandName, params string[] args)
        {
            var result = await RunCommand<Control.MuteStatus>(commandName, args);
            result.MuteState = result.ResponseItems.GetResponseItemValue("mute").GetEnumFromString<Control.MuteStateType>();
            return result;
        }

        private async Task<Control.SourceStatus> RunSourceCommand(string commandName, params string[] args)
        {
            var result = await RunCommand<Control.SourceStatus>(commandName, args);
            string source = result.ResponseItems.GetResponseItemValue("source");
            if (source != null)
            {
                result.SourceState = Control.SourceStateType.Ok;
                result.Source = source;
            }
            return result;
        }


        // Sends a command from the device schema. The command's response layout is passed as a
        // line filter so status lines the receiver sends on its own aren't mistaken for the reply.
        // SingleCapture layouts span several lines, so they can't be used as a per-line filter.
        //
        // If the receiver can't be reached or the command fails, the error is recorded for
        // DeviceErrorResultAttribute (which turns the response into a 503) and an empty reply is
        // returned. Later commands in the same request are skipped.
        private async Task<string> SendDeviceCommand(CommandItem deviceCommand)
        {
            if (HttpContext.Items.ContainsKey(DeviceErrorResultAttribute.ItemKey)) return "";

            string responseFilter = deviceCommand.ResponseCaptureType == CaptureType.SingleCapture ? null : deviceCommand.ResponseLayout;

            try
            {
                return await _connection.Send(deviceCommand.Command, deviceCommand.Terminator, deviceCommand.Timeout * _timeoutRatio, responseFilter);
            }
            catch (DeviceUnreachableException e)
            {
                _logger.LogWarning("{Message}", e.Message);
                HttpContext.Items[DeviceErrorResultAttribute.ItemKey] = e.Message;
                return "";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Receiver command failed");
                HttpContext.Items[DeviceErrorResultAttribute.ItemKey] = e.Message;
                return "";
            }
        }



        // ---------------------------------------------------------------------------------
        // Volume helpers
        // ---------------------------------------------------------------------------------

        // "45" -> 45, "455" -> 45.5 (the receiver writes half steps as three digits); -1 if not a number.
        private static decimal VolumeLevelConvert(string value)
        {
            decimal returnValue = -1;

            if (decimal.TryParse(value, out decimal d))
            {
                if (d > 99) d /= 10;
                returnValue = d;
            }

            return returnValue;
        }


        // Highest volume the API will set for a zone, in receiver units (80 = 0 dB reference,
        // 98 = +18 dB). Safety:MaxVolumeByZone:<id> overrides Safety:MaxVolume; 98 if neither is set.
        private decimal GetVolumeLimit(string zoneId)
        {
            decimal limit = _configuration.GetValue<decimal?>($"Safety:MaxVolumeByZone:{zoneId}")
                ?? _configuration.GetValue<decimal?>("Safety:MaxVolume")
                ?? 98m;

            return Math.Clamp(limit, 0m, 98m);
        }

        // 80 -> "80", 5 -> "05", 45.5 -> "455" (half steps only where the receiver supports them).
        private static string FormatVolumeLevel(decimal level, bool allowHalfSteps)
        {
            // Round down to a step the receiver accepts, so the result never exceeds the input.
            decimal stepped = allowHalfSteps ? Math.Floor(level * 2) / 2 : Math.Floor(level);

            return stepped % 1 == 0
                ? ((int)stepped).ToString("00")
                : ((int)(stepped * 10)).ToString("000");
        }


        // ---------------------------------------------------------------------------------
        // API: system
        // ---------------------------------------------------------------------------------

        // Sends any raw command to the receiver. Off by default because it bypasses all input
        // checks; set Security:EnableRawCommandEndpoint to true while debugging.
        [HttpGet("SendCommand")]
        public async Task<IActionResult> SendCommand(string command, string responseLayout, string terminator = "\x0D", int timeout = 200, CaptureType captureType = CaptureType.CaptureArray)
        {
            if (!_configuration.GetValue<bool>("Security:EnableRawCommandEndpoint")) return NotFound();

            if (string.IsNullOrEmpty(responseLayout)) responseLayout = @".*\r";
            command = Regex.Unescape(command);

            CommandItem deviceCommand = new CommandItem
            {
                Command = command?.Replace(@"\\",@"\"),
                ResponseLayout = responseLayout?.Replace(@"\\", @"\"),
                ResponseCaptureType = captureType
            };

            Control.CommandResponse commandResponse = new Control.CommandResponse
            {
                LastUpdate = DateTime.Now,
                Command = deviceCommand.Command
            };

            string telnetResponse = await _connection.Send(commandResponse.Command, terminator, timeout * _timeoutRatio);
            commandResponse.SetResponse(deviceCommand, telnetResponse);


            return new JsonResult(commandResponse);
        }


        [HttpGet("GetConfiguration")]
        public async Task<JsonResult> GetConfiguration()
        {
            ConfigurationItem config = _device.GetDeviceConfiguration();

            // Return a copy with each zone's volume limit, so the web remote can stop its dials there.
            ConfigurationItem result = new ConfigurationItem
            {
                Zones = config.Zones.Select(z => new ZoneItem
                {
                    Id = z.Id,
                    Default = z.Default,
                    Name = z.Name,
                    MaxVolume = GetVolumeLimit(z.Id)
                }).ToList(),
                Sources = config.Sources,
                Model = await GetReceiverModel(),
                ApiVersion = ApiVersion
            };

            return new JsonResult(result);
        }


        // The receiver's model as it reports it (SYMO), or the schema's model if it doesn't answer.
        // A receiver failure here is cleared rather than turned into a 503: the web remote needs
        // GetConfiguration to load its page even when the receiver is off.
        private async Task<string> GetReceiverModel()
        {
            if (_reportedModel != null) return _reportedModel;

            var status = await RunCommand<Control.ModelStatus>("GetSystemModel");
            HttpContext.Items.Remove(DeviceErrorResultAttribute.ItemKey);

            string model = status.ResponseItems.GetResponseItemValue("model")?.Trim();
            if (!string.IsNullOrEmpty(model))
            {
                _reportedModel = model;
                return model;
            }

            return _device.GetDeviceModel();
        }

        [HttpGet("GetSystemModel")]
        public async Task<JsonResult> GetSystemModel()
        {
            var result = await RunCommand<Control.ModelStatus>("GetSystemModel");
            result.Model = result.ResponseItems.GetResponseItemValue("model");
            return new JsonResult(result);
        }

        [HttpGet("CheckConnectionStatus")]
        public async Task<JsonResult> CheckConnectionStatus()
        {
            var result = await RunCommand<Control.SystemStatus>("CheckConnectionStatus");

            // This endpoint reports a failure in its body (200, SystemState = Error) rather
            // than as a 503, because that's what the web remote checks for.
            if (HttpContext.Items.Remove(DeviceErrorResultAttribute.ItemKey, out object error))
            {
                result.SetResponse(_device.GetDeviceCommand("CheckConnectionStatus"), error as string);
                result.SystemState = Control.SystemStateType.Error;
            }
            else if (string.IsNullOrEmpty(result.Response))
            {
                result.SystemState = Control.SystemStateType.Error;
                result.Response = "The receiver did not answer.";
            }
            else
            {
                result.SystemState = Control.SystemStateType.Alive;
            }

            return new JsonResult(result);
        }

        [HttpGet("GetSystemPowerStatus")]
        public async Task<JsonResult> GetSystemPowerStatus() => new JsonResult(await RunSystemPowerCommand("GetSystemPowerStatus"));

        [HttpGet("SetSystemPowerOn")]
        public async Task<JsonResult> SetSystemPowerOn() => new JsonResult(await RunSystemPowerCommand("SetSystemPowerOn"));

        [HttpGet("SetSystemPowerStandBy")]
        public async Task<JsonResult> SetSystemPowerStandBy() => new JsonResult(await RunSystemPowerCommand("SetSystemPowerStandBy"));


        // ---------------------------------------------------------------------------------
        // API: zone power
        // ---------------------------------------------------------------------------------

        [HttpGet("GetZonePowerStatus")]
        public async Task<JsonResult> GetZonePowerStatus([Required, ZoneId] string zoneId) => new JsonResult(await RunZonePowerCommand("GetZonePowerStatus", zoneId));

        [HttpGet("SetZonePowerOn")]
        public async Task<JsonResult> SetZonePowerOn([Required, ZoneId] string zoneId) => new JsonResult(await RunZonePowerCommand("SetZonePowerOn", zoneId));

        [HttpGet("SetZonePowerOff")]
        public async Task<JsonResult> SetZonePowerOff([Required, ZoneId] string zoneId) => new JsonResult(await RunZonePowerCommand("SetZonePowerOff", zoneId));


        // ---------------------------------------------------------------------------------
        // API: volume
        // ---------------------------------------------------------------------------------

        [HttpGet("GetMasterVolume")]
        public async Task<JsonResult> GetMasterVolume() => new JsonResult(await RunVolumeCommand("GetMasterVolume"));

        [HttpGet("SetMasterVolumeUp")]
        public Task<JsonResult> SetMasterVolumeUp() => SetMasterVolume("UP");

        [HttpGet("SetMasterVolumeDown")]
        public Task<JsonResult> SetMasterVolumeDown() => SetMasterVolume("DOWN");

        [HttpGet("SetMasterVolume")]
        public async Task<JsonResult> SetMasterVolume([Required, RegularExpression(InputPatterns.MasterVolume)] string value)
        {
            value = InputPatterns.NormalizeVolume(value);
            decimal limit = GetVolumeLimit("M");

            if (value == "UP")
            {
                // Don't step up if already at (or above) the limit.
                Control.VolumeStatus current = await RunVolumeCommand("GetMasterVolume");
                if (current.Level >= limit)
                {
                    _logger.LogInformation("Master volume UP ignored: level {Level} is at the limit of {Limit}", current.Level, limit);
                    return new JsonResult(current);
                }
            }
            else if (value != "DOWN" && VolumeLevelConvert(value) > limit)
            {
                _logger.LogInformation("Master volume {Requested} clamped to the limit of {Limit}", VolumeLevelConvert(value), limit);
                value = FormatVolumeLevel(limit, allowHalfSteps: true);
            }

            Control.VolumeStatus result = await RunVolumeCommand("SetMasterVolume", value);

            // Safety net: if a step still took it past the limit, pull it back.
            if (value != "DOWN" && result.Level > limit)
            {
                result = await RunVolumeCommand("SetMasterVolume", FormatVolumeLevel(limit, allowHalfSteps: true));
            }

            return new JsonResult(result);
        }

        [HttpGet("SetZoneVolumeUp")]
        public Task<JsonResult> SetZoneVolumeUp([Required, ZoneId] string zoneId) => SetZoneVolume(zoneId, "UP");

        [HttpGet("SetZoneVolumeDown")]
        public Task<JsonResult> SetZoneVolumeDown([Required, ZoneId] string zoneId) => SetZoneVolume(zoneId, "DOWN");

        [HttpGet("SetZoneVolume")]
        public async Task<JsonResult> SetZoneVolume([Required, ZoneId] string zoneId, [Required, RegularExpression(InputPatterns.ZoneVolume)] string value)
        {
            value = InputPatterns.NormalizeVolume(value);
            decimal limit = GetVolumeLimit(zoneId);

            if (value == "UP")
            {
                // Don't step up if already at (or above) the limit.
                Control.VolumeStatus current = await RunVolumeCommand("GetZonePowerStatus", zoneId);
                if (current.Level >= limit)
                {
                    _logger.LogInformation("Zone {Zone} volume UP ignored: level {Level} is at the limit of {Limit}", zoneId, current.Level, limit);
                    return new JsonResult(current);
                }
            }
            else if (value != "DOWN" && VolumeLevelConvert(value) > limit)
            {
                _logger.LogInformation("Zone {Zone} volume {Requested} clamped to the limit of {Limit}", zoneId, VolumeLevelConvert(value), limit);
                value = FormatVolumeLevel(limit, allowHalfSteps: false);
            }

            Control.VolumeStatus result = await RunVolumeCommand("SetZoneVolume", zoneId, value);

            // Safety net: if a step still took it past the limit, pull it back.
            if (value != "DOWN" && result.Level > limit)
            {
                result = await RunVolumeCommand("SetZoneVolume", zoneId, FormatVolumeLevel(limit, allowHalfSteps: false));
            }

            return new JsonResult(result);
        }


        // ---------------------------------------------------------------------------------
        // API: mute
        // ---------------------------------------------------------------------------------

        [HttpGet("GetMasterVolumeMute")]
        public async Task<JsonResult> GetMasterVolumeMute() => new JsonResult(await RunMuteCommand("GetMasterVolumeMute"));

        [HttpGet("SetMasterVolumeMuteOn")]
        public Task<JsonResult> SetMasterVolumeMuteOn() => SetMasterVolumeMute("ON");

        [HttpGet("SetMasterVolumeMuteOff")]
        public Task<JsonResult> SetMasterVolumeMuteOff() => SetMasterVolumeMute("OFF");

        [HttpGet("SetMasterVolumeMute")]
        public async Task<JsonResult> SetMasterVolumeMute([Required, RegularExpression(InputPatterns.OnOff)] string value)
            => new JsonResult(await RunMuteCommand("SetMasterVolumeMute", value.ToUpperInvariant()));

        [HttpGet("GetZoneVolumeMute")]
        public async Task<JsonResult> GetZoneVolumeMute([Required, ZoneId] string zoneId) => new JsonResult(await RunMuteCommand("GetZoneVolumeMute", zoneId));

        [HttpGet("SetZoneVolumeMuteOn")]
        public Task<JsonResult> SetZoneVolumeMuteOn([Required, ZoneId] string zoneId) => SetZoneVolumeMute(zoneId, "ON");

        [HttpGet("SetZoneVolumeMuteOff")]
        public Task<JsonResult> SetZoneVolumeMuteOff([Required, ZoneId] string zoneId) => SetZoneVolumeMute(zoneId, "OFF");

        [HttpGet("SetZoneVolumeMute")]
        public async Task<JsonResult> SetZoneVolumeMute([Required, ZoneId] string zoneId, [Required, RegularExpression(InputPatterns.OnOff)] string value)
            => new JsonResult(await RunMuteCommand("SetZoneVolumeMute", zoneId, value.ToUpperInvariant()));


        // ---------------------------------------------------------------------------------
        // API: sources
        // ---------------------------------------------------------------------------------

        // Each item: Name = the receiver's code for the input (what SetMasterSource and
        // SetZoneSource take), Value = the label to show (the custom name if set, else the default).
        [HttpGet("GetSourceList")]
        public async Task<JsonResult> GetSourceList()
            => new JsonResult(ConfiguredSourceList(useCustomNames: true) ?? await RunCommand<Control.CommandResponse>("GetSourceList"));

        // Same as GetSourceList, but always with the default labels.
        [HttpGet("GetSourceListDefault")]
        public async Task<JsonResult> GetSourceListDefault()
            => new JsonResult(ConfiguredSourceList(useCustomNames: false) ?? await RunCommand<Control.CommandResponse>("GetSourceListDefault"));

        // The source list from the schema's configuration "sources", without asking the receiver:
        // the commands for that (SSFUN ?, SSSOD ?) aren't in the AVR-4308 protocol and get no
        // answer over RS-232. Returns null if the schema has no source list, so the receiver is
        // asked instead.
        // Every source is returned, enabled or not; the web remote filters on Sources[].Enabled.
        private Control.CommandResponse ConfiguredSourceList(bool useCustomNames)
        {
            var sources = _device.GetDeviceConfiguration().Sources;
            if (sources is null || sources.Count == 0) return null;

            return new Control.SourceListResponse
            {
                LastUpdate = DateTime.Now,
                Sources = sources,
                ResponseItems = sources.Select(s => new Control.ResponseItem
                {
                    Name = s.Id,
                    Value = useCustomNames ? s.DisplayName : (string.IsNullOrWhiteSpace(s.Default) ? s.Id : s.Default.Trim())
                }).ToList()
            };
        }

        [HttpGet("GetMasterSource")]
        public async Task<JsonResult> GetMasterSource() => new JsonResult(await RunSourceCommand("GetMasterSource"));

        [HttpGet("SetMasterSource")]
        public async Task<JsonResult> SetMasterSource([Required, RegularExpression(InputPatterns.Source)] string source)
            => new JsonResult(await RunCommand<Control.CommandResponse>("SetMasterSource", source.ToUpperInvariant()));

        [HttpGet("GetZoneSource")]
        public async Task<JsonResult> GetZoneSource([Required, ZoneId] string zoneId) => new JsonResult(await RunSourceCommand("GetZoneSource", zoneId));

        [HttpGet("SetZoneSource")]
        public async Task<JsonResult> SetZoneSource([Required, ZoneId] string zoneId, [Required, RegularExpression(InputPatterns.Source)] string source)
            => new JsonResult(await RunCommand<Control.CommandResponse>("SetZoneSource", zoneId, source.ToUpperInvariant()));
    }
}
