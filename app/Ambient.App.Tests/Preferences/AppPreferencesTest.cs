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
    public void TheDocumentCarriesItsSchemaVersion()
    {
        var store = new MemoryPreferencesStore();
        new AppPreferences(store).Save();

        var version = JsonDocument.Parse(store.Json!).RootElement.GetProperty("SchemaVersion").GetInt32();
        Assert.Equal(AppPreferences.CurrentSchema, version);
    }

    [Fact]
    public void ACorruptDocumentMeansDefaultsAndALogLine()
    {
        var store = new MemoryPreferencesStore { Json = "{ this is not json" };
        var log = new ListLogger();

        var loaded = AppPreferences.Load(store, log);

        Assert.False(loaded.KeepConsultations);
        Assert.Equal("system", loaded.Theme);
        Assert.Contains(log.Lines, line => line.Contains("preferences unreadable"));
    }

    [Fact]
    public void ANewerDocumentIsReadForWhatThisBuildKnows()
    {
        var store = new MemoryPreferencesStore
        {
            Json = """{"SchemaVersion":99,"KeepConsultations":true,"FutureSetting":"x"}""",
        };

        var loaded = AppPreferences.Load(store);

        Assert.True(loaded.KeepConsultations);
    }

    [Fact]
    public void ValuesNeitherSideAcceptsFallBackToTheDefault()
    {
        var store = new MemoryPreferencesStore
        {
            Json = """{"Theme":"solarized","NoteStyle":"haiku","NoteDetail":"verbose","NoteTier":"premium"}""",
        };

        var loaded = AppPreferences.Load(store);

        Assert.Equal("system", loaded.Theme);
        Assert.Equal("prose", loaded.NoteStyle);
        Assert.Equal("standard", loaded.NoteDetail);
        Assert.Equal("default", loaded.NoteTier);
    }

    [Fact]
    public void NothingStoredMeansDefaultsAndNoWrite()
    {
        var store = new MemoryPreferencesStore();

        var loaded = AppPreferences.Load(store);

        Assert.False(loaded.DemoTrayEnabled);
        Assert.Null(store.Json);
    }
}
