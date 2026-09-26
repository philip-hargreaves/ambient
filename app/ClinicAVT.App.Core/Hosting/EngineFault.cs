namespace ClinicAVT.App.Core.Hosting;

public enum EngineFaultKind
{
    CrashLoop,
    LaunchFailed,
}

public sealed record EngineFault(EngineFaultKind Kind, int? ExitCode = null);
