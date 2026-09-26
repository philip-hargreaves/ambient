using ClinicAVT.App.Core.Hosting;

namespace ClinicAVT.App.Core.Ports;

public interface ICrashLog
{
    void Record(CrashReport report);
}
