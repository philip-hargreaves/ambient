using System.Diagnostics;
using Ambient.App.Platform;

namespace Ambient.App.Tests.Hosting;

public class PowerThrottlingTest
{
    private static readonly string[] States = ["off", "on", "default", "unknown"];

    [Fact]
    public void TheCurrentProcessCanBeReadAndOptedOut()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
        {
            return;
        }

        using var process = Process.GetCurrentProcess();

        Assert.Contains(PowerThrottling.Describe(process.SafeHandle), States);
        Assert.True(PowerThrottling.Disable(process.SafeHandle));
        Assert.Equal("off", PowerThrottling.Describe(process.SafeHandle));
    }
}
