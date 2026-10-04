using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Denon.Web.Api
{
    /// <summary>
    /// Allowed formats for values that end up inside telnet commands. Anything else is
    /// rejected with a 400 before it reaches the receiver.
    /// </summary>
    public static class InputPatterns
    {
        // Master volume: UP, DOWN, 0-98, or a half step written as three digits (455 = 45.5), up to 975.
        public const string MasterVolume = @"(?i)^(UP|DOWN|[0-8]?[0-9]|9[0-8]|[0-8][0-9]5|9[0-7]5)$";

        // Zone volume: UP, DOWN, or 0-98.
        public const string ZoneVolume = @"(?i)^(UP|DOWN|[0-8]?[0-9]|9[0-8])$";

        public const string OnOff = @"(?i)^(ON|OFF)$";

        // Source names as the receiver reports them (CD, TV/CBL, V.AUX, NET/USB, ...).
        public const string Source = @"(?i)^[A-Z0-9./]{1,12}$";

        /// <summary>Upper-cases and pads single-digit levels to the two digits the receiver expects ("5" becomes "05").</summary>
        public static string NormalizeVolume(string value)
        {
            value = value.ToUpperInvariant();
            return value.Length == 1 && char.IsDigit(value[0]) ? "0" + value : value;
        }
    }


    /// <summary>
    /// Validates that a zone id is one of the zones in the device schema.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public sealed class ZoneIdAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value is null) return ValidationResult.Success; // [Required] handles missing values

            var device = (IDenonDevice)validationContext.GetService(typeof(IDenonDevice));
            var zoneIds = device?.GetDeviceConfiguration()?.Zones?.Select(z => z.Id).ToList();

            if (zoneIds is null || !zoneIds.Contains(value as string, StringComparer.Ordinal))
            {
                return new ValidationResult($"Unknown zone '{value}'. Valid zones: {string.Join(", ", zoneIds ?? new())}.");
            }

            return ValidationResult.Success;
        }
    }
}
