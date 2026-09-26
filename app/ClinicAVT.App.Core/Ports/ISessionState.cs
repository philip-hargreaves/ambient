namespace ClinicAVT.App.Core.Ports;

public interface ISessionState
{
    /// <summary>True when an engine death would interrupt a consultation in progress.</summary>
    bool ConsultationActive { get; }

    /// <summary>Where the session was, for the crash log. Empty when unknown.</summary>
    string SessionPhase => "";
}
