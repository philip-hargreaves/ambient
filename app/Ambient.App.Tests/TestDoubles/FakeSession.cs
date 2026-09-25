using Ambient.App.Core.Ports;

namespace Ambient.App.Tests.TestDoubles;

public sealed class FakeSession : ISessionState
{
    public bool ConsultationActive { get; set; }

    public string SessionPhase { get; set; } = "";
}
