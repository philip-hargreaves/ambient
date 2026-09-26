namespace ClinicAVT.App.Core.Metrics;

public sealed record GpuInfo(string Name, string Driver);

public sealed record MachineInfo(
    string Cpu,
    int RamGb,
    string Os,
    IReadOnlyList<GpuInfo> Gpus,
    GpuInfo? Npu);
