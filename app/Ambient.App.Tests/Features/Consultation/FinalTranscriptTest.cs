using Ambient.App.Tests.Support;

namespace Ambient.App.Tests.Features.Consultation;

public class FinalTranscriptTest
{
    [Fact]
    public async Task StopReplacesThePaneWithTheLabelledTranscript()
    {
        var (session, engine, _) = TestSession.Create();
        engine.Transcript.Add(("doctor", "how long has the knee been swollen"));
        engine.Transcript.Add(("patient", "about three weeks now"));

        await session.StartRecordingAsync();
        await session.StopRecordingAsync();

        Assert.Equal(2, session.Transcript.Turns.Count);
        Assert.Equal("doctor", session.Transcript.Turns[0].Speaker);
        Assert.Equal("Doctor", session.Transcript.Turns[0].SpeakerLabel);
        Assert.Equal("how long has the knee been swollen", session.Transcript.Turns[0].Text);
        Assert.Equal("patient", session.Transcript.Turns[1].Speaker);
    }
}
