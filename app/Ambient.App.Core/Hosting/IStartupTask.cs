namespace Ambient.App.Core.Hosting;

/// <summary>When a startup task runs: before the window exists, or once it is on screen.</summary>
public enum StartupStage
{
    BeforeWindow,
    AfterWindow,
}

/// <summary>One step of launch, run in registration order within its stage. A failure is logged and the next step runs.</summary>
public interface IStartupTask
{
    string Name { get; }

    StartupStage Stage { get; }

    void Run();
}
