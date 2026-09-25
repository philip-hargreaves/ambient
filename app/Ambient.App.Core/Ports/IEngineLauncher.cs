namespace Ambient.App.Core.Ports;

/// <summary>Starts one engine process, already bound so it cannot outlive the app.</summary>
public interface IEngineLauncher
{
    IEngineProcess Launch();
}
