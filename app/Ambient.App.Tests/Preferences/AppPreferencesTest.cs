using System.Text.Json;
using Ambient.App.Core.Preferences;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Preferences;

public class AppPreferencesTest
{
    [Fact]
    public void EveryValueRoundTripsThroughTheStore()
    {
        var store = new MemoryPreferencesStore();
        var preferences = new AppPreferences(store)
        {
            DemoTrayEnabled = true,
            DemoMode = true,
            DemoTrack = "Elbow swelling",
            SeedDataEnabled = true,
            NpuTranscription = true,
            CollectPerformanceData = true,
            KeepConsultations = true,
            ShowPerformanceMetrics = true,
            IncludeResearchGuidance = true,
            MicId = "{mic-7}",
            Theme = "dark",
            NoteStyle = "soap",
            NoteDetail = "detailed",
            NoteTier = "accuracy",
        };

        preferences.Save();
        var loaded = AppPreferences.Load(store);

        var version = JsonDocument.Parse(store.Json!).RootElement.GetProperty("SchemaVersion").GetInt32();
        Assert.Equal(AppPreferences.CurrentSchema, version);
        Assert.True(loaded.DemoTrayEnabled);
        Assert.True(loaded.DemoMode);
        Assert.Equal("Elbow swelling", loaded.DemoTrack);
        Assert.True(loaded.SeedDataEnabled);
        Assert.True(loaded.NpuTranscription);
        Assert.True(loaded.CollectPerformanceData);
        Assert.True(loaded.KeepConsultations);
        Assert.True(loaded.ShowPerformanceMetrics);
        Assert.True(loaded.IncludeResearchGuidance);
        Assert.Equal("{mic-7}", loaded.MicId);
        Assert.Equal("dark", loaded.Theme);
        Assert.Equal("soap", loaded.NoteStyle);
        Assert.Equal("detailed", loaded.NoteDetail);
        Assert.Equal("accuracy", loaded.NoteTier);
    }

    [Fact]
    public void NothingStoredOrUnreadableMeansDefaults()
    {
        var empty = new MemoryPreferencesStore();
        var loaded = AppPreferences.Load(empty);
        Assert.False(loaded.DemoTrayEnabled);
        Assert.Null(empty.Json);  // no write on load

        var corrupt = new MemoryPreferencesStore { Json = "{ this is not json" };
        var log = new ListLogger();
        loaded = AppPreferences.Load(corrupt, log);
        Assert.False(loaded.KeepConsultations);
        Assert.Equal("system", loaded.Theme);
        Assert.Contains(log.Lines, line => line.Contains("preferences unreadable"));
    }

    [Fact]
    public void ANewerDocumentIsReadForWhatThisBuildKnowsAndUnknownValuesFallBack()
    {
        var newer = new MemoryPreferencesStore
        {
            Json = """{"SchemaVersion":99,"KeepConsultations":true,"FutureSetting":"x"}""",
        };
        Assert.True(AppPreferences.Load(newer).KeepConsultations);

        var odd = new MemoryPreferencesStore
        {
            Json = """{"Theme":"solarized","NoteStyle":"haiku","NoteDetail":"verbose","NoteTier":"premium"}""",
        };
        var loaded = AppPreferences.Load(odd);
        Assert.Equal("system", loaded.Theme);
        Assert.Equal("prose", loaded.NoteStyle);
        Assert.Equal("standard", loaded.NoteDetail);
        Assert.Equal("default", loaded.NoteTier);
    }
}
