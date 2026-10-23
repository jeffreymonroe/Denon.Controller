using System;
using System.Collections.Generic;
using System.Text;
using static Denon.Serial.Test.Extensions.AssemblyExtensions;

namespace Denon.Serial.Test;

public class AssemblyMarker
{
    string appName = ApplicationName(typeof(AssemblyMarker));
    string appVersion = ApplicationVersion(typeof(AssemblyMarker));
    string appNameVersion = ApplicationNameVersion(typeof(AssemblyMarker));

    public string AppName { get { return appName; } }
    public string AppVersion { get { return appVersion; } }
    public string AppNameVersion { get { return appNameVersion; } }
}
