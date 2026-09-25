using Ambient.App.Core.Common;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Tests.Support;

namespace Ambient.App.Tests.Features.Consultation;

public class ConsultationHeaderTest
{
    [Fact]
    public async Task TheHeadingIsTheStartTimeOfTheRecording()
    {
        var (session, _, _) = TestSession.Create();
        var started = new DateTimeOffset(2026, 9, 25, 9, 31, 0, TimeSpan.Zero);
        var header = new ConsultationHeaderViewModel(session, () => started);
        Assert.Equal("", header.Title);

        await session.StartRecordingAsync();

        Assert.Equal(SessionText.Heading(started), header.Title);
        Assert.Contains("September", header.Title);
    }
}
