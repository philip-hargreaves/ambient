using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Preferences;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;


namespace Ambient.App.Tests.Features.Consultation;

public class MicViewModelTest
{
    private static AppPreferences TempPreferences() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

    private static MicDevice Array(bool isDefault = true) =>
        new("{aa}", "Microphone Array (Cirrus Logic)", "Microphone Array", isDefault, false);

    private static MicDevice Jabra() =>
        new("{bb}", "Headset (Jabra Evolve2 65)", "Headset", false, true);

    [Fact]
    public async Task TheLabelSaysWhenThereIsNoMicrophoneAndNamesOneDeviceAsShortAsThatAllows()
    {
        var engine = new FakeEngineClient();
        var mic = new MicViewModel(new EngineApi(engine));

        await mic.RefreshAsync();
        Assert.False(mic.HasDevices);
        Assert.Empty(mic.Rows);
        Assert.Equal("No microphone found", mic.Label);
        Assert.Equal("No microphone found - connect one to record", mic.NoDevicesText);
        Assert.Equal("", mic.MicId);

        // A single-microphone laptop, the common clinical case, reads cleanly
        engine.AudioInputs = [Array()];
        await mic.RefreshAsync();
        Assert.Equal("Microphone Array", mic.Label);

        // Two devices, distinct endpoints: still just the endpoints. The rows name the
        // default, check the current choice and caption the Bluetooth one
        engine.AudioInputs = [Array(), Jabra()];
        await mic.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Microphone Array", mic.Label);
        Assert.Equal(
            [("Microphone Array (Cirrus Logic)  (default)", "", true), ("Headset (Jabra Evolve2 65)", "    Bluetooth call mode - reduced recording quality", false)],
            mic.Rows.Select(r => (r.Label, r.Note, r.IsChecked)));

        // Three devices all called "Microphone": the full name disambiguates
        engine.AudioInputs =
        [
            new("{aa}", "Microphone (USB Audio)", "Microphone", true, false),
            new("{bb}", "Microphone (Jabra)", "Microphone", false, true),
            new("{cc}", "Microphone (Realtek)", "Microphone", false, false),
        ];
        await mic.RefreshAsync();
        Assert.Equal("Microphone (USB Audio)", mic.Label);
    }

    [Fact]
    public async Task TheChoicePersistsAGoneChoiceFallsToTheDefaultAndStartSendsTheSavedOne()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineClient();
        var mic = new MicViewModel(new EngineApi(engine), preferences);
        engine.AudioInputs = [Array(), Jabra()];
        await mic.RefreshAsync();

        mic.SelectCommand.Execute("{bb}");
        Assert.Equal("{bb}", mic.MicId);
        Assert.Equal("{bb}", preferences.MicId);
        Assert.Equal([false, true], mic.Rows.Select(r => r.IsChecked));

        // The headset is unplugged: the default speaks for it, but the
        // saved choice survives for when it comes back
        engine.AudioInputs = [Array()];
        await mic.RefreshAsync();
        Assert.Equal("{aa}", mic.MicId);
        Assert.Equal("{bb}", preferences.MicId);

        engine.AudioInputs = [Array(), Jabra()];
        await mic.RefreshAsync();
        Assert.Equal("{bb}", mic.MicId);

        var (session, sessionEngine, _) = TestSession.Create(preferences);
        await session.StartRecordingAsync();
        var start = sessionEngine.Requests.Single(r => r.Method == "session/start");
        Assert.Contains("{bb}", start.Params);
    }
}
