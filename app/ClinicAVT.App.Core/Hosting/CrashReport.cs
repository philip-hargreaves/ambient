namespace ClinicAVT.App.Core.Hosting;

/// <summary>
/// One engine death. It holds metadata and no process memory, so the log can go in a
/// support bundle. MethodInFlight is the request outstanding at the time, and
/// SessionPhase is what the consultation was doing.
/// </summary>
public sealed record CrashReport(
    DateTimeOffset Timestamp,
    int ExitCode,
    TimeSpan Uptime,
    int CrashCount,
    RecoveryAction Action,
    string? MethodInFlight = null,
    string? SessionPhase = null);
