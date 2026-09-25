namespace Ambient.App.Core.Hosting;

/// <summary>When a startup task runs: before the window exists, or once it is on screen.</summary>
public enum StartupStage
{
    BeforeWindow,
    AfterWindow,
}
