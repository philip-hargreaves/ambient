using Ambient.App.Core.Hosting;

namespace Ambient.App.Core.Ports;

public interface ICrashLog
{
    void Record(CrashReport report);
}
