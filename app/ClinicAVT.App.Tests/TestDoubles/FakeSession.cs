using ClinicAVT.App.Core.Ports;

namespace ClinicAVT.App.Tests.TestDoubles;

public sealed class FakeSession : ISessionState
{
    public bool ConsultationActive { get; set; }

    public string SessionPhase { get; set; } = "";
}
