namespace Ambient.App.Core.Ports;

/// <summary>Memory figures of the product's processes, for the metrics log and the memory chip.</summary>
public interface IProcessMetrics
{
    long? PeakWorkingSetMb(int pid);

    long? PeakCommitMb(int pid);

    /// <summary>The largest peak among the processes of that name; null when none runs.</summary>
    long? PeakWorkingSetMbOf(string processName);

    /// <summary>This process plus every process of the named images, in GB.</summary>
    double WorkingSetGb(params string[] processNames);
}

/// <summary>No figures at all: what a test or a collector without a reader gets.</summary>
public sealed class NoProcessMetrics : IProcessMetrics
{
    public long? PeakWorkingSetMb(int pid) => null;

    public long? PeakCommitMb(int pid) => null;

    public long? PeakWorkingSetMbOf(string processName) => null;

    public double WorkingSetGb(params string[] processNames) => 0;
}
