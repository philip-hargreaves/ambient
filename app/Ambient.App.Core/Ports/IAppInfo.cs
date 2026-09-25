namespace Ambient.App.Core.Ports;

/// <summary>What the build says about itself.</summary>
public interface IAppInfo
{
    /// <summary>"0.4.2": the assembly version, three parts.</summary>
    string Version { get; }
}
