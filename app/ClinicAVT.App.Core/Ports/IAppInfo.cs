namespace ClinicAVT.App.Core.Ports;

public interface IAppInfo
{
    /// <summary>The assembly version in three parts, such as "0.4.2".</summary>
    string Version { get; }
}
