using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Features.Help;

/// <summary>The Help page: the guide is markup, this is the small print about the build.</summary>
public sealed class HelpViewModel(IAppInfo app)
{
    public string VersionLine { get; } = $"Version {app.Version}, for demonstration and evaluation";
}
