using ClinicAVT.App.Core.Hosting;

namespace ClinicAVT.App.Core.Ports;

/// <summary>
/// One step of launch, run in registration order within its stage. A failure is logged
/// and the next step runs.
/// </summary>
public interface IStartupTask
{
    string Name { get; }

    StartupStage Stage { get; }

    void Run();
}
