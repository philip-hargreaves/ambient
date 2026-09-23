using System.Diagnostics;
using Ambient.App.Core.Ports;

namespace Ambient.App.Platform;

/// <summary>Memory figures through System.Diagnostics, where a process that has exited reads as null.</summary>
public sealed class ProcessMetrics : IProcessMetrics
{
    public long? PeakWorkingSetMb(int pid) => Of(pid, p => p.PeakWorkingSet64);

    public long? PeakCommitMb(int pid) => Of(pid, p => p.PeakPagedMemorySize64);

    public long? PeakWorkingSetMbOf(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length == 0
                    ? null
                    : processes.Max(p => p.PeakWorkingSet64) / (1024 * 1024);
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public double WorkingSetGb(params string[] processNames)
    {
        try
        {
            var bytes = Environment.WorkingSet;
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        bytes += process.WorkingSet64;
                    }
                }
            }

            return bytes / (1024.0 * 1024 * 1024);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static long? Of(int pid, Func<Process, long> metric)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return metric(process) / (1024 * 1024);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
