using Ambient.App.Core.Ports;

namespace Ambient.App.Platform;

/// <summary>The version the shell assembly was built with.</summary>
public sealed class AppInfo : IAppInfo
{
    public string Version { get; } = Describe(typeof(AppInfo).Assembly.GetName().Version);

    private static string Describe(Version? version)
    {
        version ??= new Version(0, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
