using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Metrics;

/// <summary>No figures at all: what a test or a collector without a reader gets.</summary>
public sealed class NoProcessMetrics : IProcessMetrics
{
    public long? PeakWorkingSetMb(int pid) => null;

    public long? PeakCommitMb(int pid) => null;

    public long? PeakWorkingSetMbOf(string processName) => null;

    public double WorkingSetGb(params string[] processNames) => 0;
}
