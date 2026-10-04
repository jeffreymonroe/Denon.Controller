using Denon.Core.Command;
using Microsoft.Extensions.Configuration;
using System;

namespace Denon.Web.Api
{
    /// <summary>
    /// Creates the receiver connection chosen by DeviceConfig:ConnectionType.
    /// </summary>
    public static class DenonConnectionFactory
    {
        public static IDenonConnection Create(IConfiguration configuration)
        {
            string type = configuration["DeviceConfig:ConnectionType"];
            if (string.IsNullOrWhiteSpace(type)) type = "Telnet";   // the behaviour before this setting existed

            switch (type.Trim().ToLowerInvariant())
            {
                case "telnet":
                    return new TcpConnection(
                        configuration["DeviceConfig:Host"],
                        configuration.GetValue("DeviceConfig:TelnetPort", 23),
                        configuration.GetValue("DeviceConfig:TelnetConnectionTimeout", 300000L));

                case "serial":
                    SerialSettings settings = configuration.GetSection("DeviceConfig:Serial").Get<SerialSettings>() ?? new SerialSettings();
                    return new SerialConnection(settings);

                default:
                    throw new InvalidOperationException($"DeviceConfig:ConnectionType '{type}' is not supported. Use 'Telnet' or 'Serial'.");
            }
        }
    }
}
