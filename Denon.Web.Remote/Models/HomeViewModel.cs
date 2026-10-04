using Denon.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Denon.Web.Remote.Models
{
    public class HomeViewModel
    {
        public string RemoteWebApi { get; set; }
        public int TimeoutRatio { get; set; } = 1;

        public ConfigurationItem Configuration { get; set; }

        // Set when the configuration couldn't be loaded from the API; the page shows it instead of the controls.
        public string StartupError { get; set; }

        // This web remote's version (from Directory.Build.props and the git commit).
        public string RemoteVersion { get; set; }

        // "1.1.0+96afac9e1c2d..." -> "1.1.0+96afac9" (a full commit hash is too long for the footer).
        public static string ShortVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return "?";
            int plus = version.IndexOf('+');
            return plus < 0 || version.Length <= plus + 8 ? version : version.Substring(0, plus + 8);
        }
    }
}
