using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Denon.Serial.Test.Extensions;

public static class AssemblyExtensions
{
    public static string ApplicationNameVersion(Type t)
    {
        return $"{ApplicationName(t)} - {ApplicationVersion(t)}";
    }

    public static string ApplicationName(Type t)
    {
        string? assemblyName = t.Assembly.GetName().Name;
        string name = (assemblyName is not null) ? assemblyName : "Unknown";

        return name;
    }

    public static string ApplicationVersion(Type t)
    {
        AssemblyInformationalVersionAttribute? versionAttribute = t.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        string? assemblyVersion = (versionAttribute is not null) ? versionAttribute.InformationalVersion : null;
        string version = (assemblyVersion is not null) ? assemblyVersion : "0.0.0";

        return $"v{version}";
    }
}
