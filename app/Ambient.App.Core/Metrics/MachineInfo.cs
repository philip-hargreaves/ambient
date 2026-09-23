namespace Ambient.App.Core.Metrics;

public sealed record GpuInfo(string Name, string Driver);

public sealed record MachineInfo(
    string Cpu,
    int RamGb,
    string Os,
    IReadOnlyList<GpuInfo> Gpus,
    GpuInfo? Npu);

/// <summary>Hardware identity for the performance report; queried once.</summary>
public interface IMachineInfoProvider
{
    MachineInfo Describe();
}
