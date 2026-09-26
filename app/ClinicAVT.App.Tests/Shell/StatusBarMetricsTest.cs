using ClinicAVT.App.Core.Shell;
using ClinicAVT.App.Tests.TestDoubles;
using ClinicAVT.Client;
using static ClinicAVT.App.Tests.Support.Waits;
using static ClinicAVT.App.Tests.Support.Wire;

namespace ClinicAVT.App.Tests.Shell;

/// <summary>The live numbers behind the status bar's model chips.</summary>
public class StatusBarMetricsTest
{
    private static (StatusBarViewModel Status, FakeEngineClient Engine) Create(Func<double>? memoryGb = null)
    {
        var engine = new FakeEngineClient(autoNotify: false);
        return (new StatusBarViewModel(new EngineApi(engine), new InlineDispatcher(), memoryGb: memoryGb), engine);
    }

    [Fact]
    public async Task TheNoteChipThroughAGeneration()
    {
        var (status, engine) = Create();
        await WaitUntilAsync(() => status.NoteChip.Length > 0);
        Assert.Equal("Qwen3.5 9B · GPU", status.NoteChip);
        Assert.False(status.TokensStreaming);
        Assert.False(status.NoteActive);

        // The engine meters at the source, before its 12 Hz throttle, and the
        // shell shows that figure
        engine.RaiseNotification("note/partial",
            Params(new { text = "The", tokensPerSecond = 15.3 }));
        Assert.True(status.TokensStreaming);
        Assert.True(status.NoteActive);
        Assert.Equal(15.3, status.TokensPerSecond);
        Assert.Contains("15.3 tok/s", status.NoteChip);

        // The ready event carries the whole generation's average, which
        // holds and is labelled as what it is
        engine.RaiseNotification("note/ready",
            Params(new { text = "The note.", tokensPerSecond = 14.2 }));
        Assert.Equal(14.2, status.TokensPerSecond);
        Assert.False(status.TokensStreaming, "the stream ended and the value holds");
        Assert.False(status.NoteActive);
        Assert.Contains("Averaged 14.2 tok/s", status.NoteChip);

        // A failure ends the stream too, and a translation meters the same way
        engine.RaiseNotification("note/partial", Params(new { text = "The" }));
        Assert.True(status.TokensStreaming);
        engine.RaiseNotification("note/failed");
        Assert.False(status.TokensStreaming);

        engine.RaiseNotification("translate/partial", Params(new { text = "Twoja" }));
        Assert.True(status.TokensStreaming);
        engine.RaiseNotification("translate/failed");
        Assert.False(status.TokensStreaming);

        // A new consultation clears the frozen value
        status.ResetThroughput();
        Assert.Equal("Qwen3.5 9B · GPU", status.NoteChip);
        Assert.False(status.TokensStreaming);
        Assert.Equal(0, status.TokensPerSecond);

        // A tier switch renames the chip even when the engine is too busy to
        // answer the store call
        engine.Failing.Add("engine/models");
        engine.RaiseNotification("note/model", Params(new
        {
            state = "ready",
            tier = "accuracy",
            id = "qwen3.6-35b-a3b-int4",
            name = "Qwen3.6 35B",
            seconds = 62.0,
        }));
        Assert.Equal("Qwen3.6 35B · GPU", status.NoteChip);
    }

    [Fact]
    public async Task TheAsrAndMemoryChipsThroughAConsultation()
    {
        var reading = 5.06;
        var (status, engine) = Create(memoryGb: () => reading);
        await WaitUntilAsync(() => status.AsrChip.Length > 0);  // the connect-time model fetch
        Assert.Equal("Whisper Large v3 Turbo · GPU", status.AsrChip);
        Assert.Equal("Qwen3.5 9B · GPU", status.NoteChip);
        Assert.False(status.AsrActive);

        // While idle the poll reads the engine's figure, but a low one would not warn
        await status.PollMetricsOnceAsync();
        Assert.Equal(33.4, status.RealtimeFactor);  // FakeEngineClient's figure
        Assert.False(status.RealtimeLow, "not recording: no warning");
        Assert.Equal("Memory · 5.1 GB", status.MemoryChip);

        // While recording the realtime factor joins Whisper's chip and the dot lights
        status.SetMicVisible(true);
        Assert.True(status.AsrActive);
        await status.PollMetricsOnceAsync();
        Assert.Equal("Whisper Large v3 Turbo · GPU · 33× RT", status.AsrChip);
        Assert.False(status.RealtimeLow, "33x is healthy");

        // A slow factor keeps a decimal and warns
        engine.MetricsRealtimeFactor = 1.4;
        await status.PollMetricsOnceAsync();
        Assert.Equal("Whisper Large v3 Turbo · GPU · 1.4× RT", status.AsrChip);
        Assert.True(status.RealtimeLow);

        // After stop the tail still decodes, the NPU's longest stage, so the
        // figure, the warning and the dot stay until the transcript seals
        status.SetMicVisible(false);
        status.SetDecodeActive(true);
        Assert.True(status.AsrActive);
        Assert.Equal("Whisper Large v3 Turbo · GPU · 1.4× RT", status.AsrChip);
        Assert.True(status.RealtimeLow);

        // Once sealed the session's average holds, labelled as an average
        status.SetDecodeActive(false);
        Assert.False(status.AsrActive, "sealed: the dot rests while Averaged shows");
        Assert.Equal("Whisper Large v3 Turbo · GPU · Averaged 1.4× RT", status.AsrChip);
        Assert.False(status.RealtimeLow, "the warning is a transcribing-time signal");

        status.ResetThroughput();  // the next consultation starts clean
        Assert.Equal("Whisper Large v3 Turbo · GPU", status.AsrChip);

        reading = 0;  // the memory provider failed, so no figure shows
        await status.PollMetricsOnceAsync();
        Assert.Equal("", status.MemoryChip);
    }
}
