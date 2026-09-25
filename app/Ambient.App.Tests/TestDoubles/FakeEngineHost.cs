using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;

namespace Ambient.App.Tests.TestDoubles;

/// <summary>An engine host whose status the test moves by hand, or through Start and Shutdown.</summary>
public sealed class FakeEngineHost : IEngineHost
{
    public event Action<EngineStatus>? StatusChanged;

    public EngineStatus Status { get; private set; } = EngineStatus.Stopped;

    public EngineFault? Fault => null;

    public int? EnginePid { get; set; }

    public List<string> Calls { get; } = [];

    public void Start()
    {
        Calls.Add("start");
        RaiseStatus(EngineStatus.Running);
    }

    public void Shutdown()
    {
        Calls.Add("shutdown");
        RaiseStatus(EngineStatus.Stopped);
    }

    public void RaiseStatus(EngineStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}
