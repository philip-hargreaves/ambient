using Ambient.App.Core.Metrics;

namespace Ambient.App.Core.Ports;

/// <summary>Hardware identity for the performance report, queried once.</summary>
public interface IMachineInfoProvider
{
    MachineInfo Describe();
}
