using ClinicAVT.App.Core.Hosting;

namespace ClinicAVT.App.Core.Ports;

/// <summary>
/// The shell's port to the engine process lifecycle. StatusChanged may fire on
/// background threads.
/// </summary>
public interface IEngineHost
{
    event Action<EngineStatus>? StatusChanged;

    EngineStatus Status { get; }

    EngineFault? Fault { get; }

    int? EnginePid { get; }

    void Start();

    void Shutdown();
}
