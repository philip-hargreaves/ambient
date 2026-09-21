using System.Text.Json;

namespace Ambient.Client.Tests;

/// <summary>The fixtures both languages must agree on.</summary>
public class FixtureTest
{
    private static JsonDocument LoadFixture(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "schema", "fixtures")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir, "schema", "fixtures", name)));
    }

    [Fact]
    public void PeerInfoSerializesToHelloRequestParams()
    {
        var serialized = JsonSerializer.SerializeToElement(
            new PeerInfo("ambient-shell", "0.1.0", Protocol.ProtocolVersion),
            Protocol.JsonOptions);
        var expected = LoadFixture("hello-request.json").RootElement.GetProperty("params");

        Assert.True(JsonElement.DeepEquals(serialized, expected));
    }

    [Fact]
    public void HelloResponseFixtureDeserializesToPeerInfo()
    {
        var result = LoadFixture("hello-response.json").RootElement.GetProperty("result");
        var peer = result.Deserialize<PeerInfo>(Protocol.JsonOptions);

        // Against the constants, so drift from the shared fixture fails here
        Assert.Equal(
            new PeerInfo(EngineInfo.Name, EngineInfo.Version, Protocol.ProtocolVersion), peer);
    }

    [Fact]
    public void EchoFixturePayloadSurvivesRoundTrip()
    {
        var fixture = LoadFixture("echo-request-nonascii.json");
        var payload = fixture.RootElement.GetProperty("params").GetProperty("payload").GetString();
        Assert.NotNull(payload);

        var reserialized = JsonSerializer.Serialize(new { payload }, Protocol.JsonOptions);
        Assert.Contains("naïve", reserialized, StringComparison.Ordinal);
        Assert.Contains("東京", reserialized, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioLevelFixtureCarriesLevelAndClip()
    {
        var root = LoadFixture("audio-level.json").RootElement;

        Assert.Equal("audio.level", root.GetProperty("method").GetString());
        Assert.Equal(0.5, root.GetProperty("params").GetProperty("level").GetDouble());
        Assert.False(root.GetProperty("params").GetProperty("clipped").GetBoolean());
    }

    [Fact]
    public void PlaybackFixturesNameTheSessionAndCarryTheClock()
    {
        var start = LoadFixture("session-start-playback.json").RootElement;
        Assert.Equal("session/start", start.GetProperty("method").GetString());
        Assert.False(string.IsNullOrEmpty(
            start.GetProperty("params").GetProperty("playback").GetProperty("id").GetString()));

        var level = LoadFixture("audio-level-playback.json").RootElement;
        Assert.Equal("audio.level", level.GetProperty("method").GetString());
        Assert.Equal(271.4, level.GetProperty("params").GetProperty("seconds").GetDouble());
    }

    [Fact]
    public void SessionInterruptedFixtureCarriesReasonAndDetail()
    {
        var root = LoadFixture("session-interrupted.json").RootElement;

        Assert.Equal("session/interrupted", root.GetProperty("method").GetString());
        Assert.Equal("deviceLost", root.GetProperty("params").GetProperty("reason").GetString());
        Assert.False(
            string.IsNullOrEmpty(root.GetProperty("params").GetProperty("detail").GetString()));
    }

    [Fact]
    public void SessionProgressFixtureCarriesStage()
    {
        var root = LoadFixture("session-progress.json").RootElement;

        Assert.Equal("session/progress", root.GetProperty("method").GetString());
        Assert.Equal("speakers", root.GetProperty("params").GetProperty("stage").GetString());
    }

    [Fact]
    public void SessionListFixtureCarriesTheLabelAndEditStamp()
    {
        var session = LoadFixture("session-list.json").RootElement
            .GetProperty("result").GetProperty("sessions")[0];

        Assert.Equal("finalised", session.GetProperty("state").GetString());
        Assert.False(string.IsNullOrEmpty(session.GetProperty("label").GetString()));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("editedAt").ValueKind);
    }

    [Fact]
    public void DemoFixturesCarryTheCountsAndTheSampleFlag()
    {
        var seeded = LoadFixture("demo-seed.json").RootElement.GetProperty("result");
        var cleared = LoadFixture("demo-clear.json").RootElement.GetProperty("result");
        Assert.Equal(seeded.GetProperty("added").GetInt32(), cleared.GetProperty("removed").GetInt32());

        var session = LoadFixture("session-list.json").RootElement
            .GetProperty("result").GetProperty("sessions")[0];
        Assert.False(session.GetProperty("demo").GetBoolean());
        var entry = LoadFixture("reflection-list.json").RootElement
            .GetProperty("result").GetProperty("reflections")[0];
        Assert.False(entry.GetProperty("demo").GetBoolean());
    }

    [Fact]
    public void SessionNoteFixtureCarriesTheRecordFields()
    {
        var note = LoadFixture("session-note.json").RootElement.GetProperty("result");

        Assert.False(string.IsNullOrEmpty(note.GetProperty("text").GetString()));
        Assert.Equal("prose", note.GetProperty("style").GetString());
        Assert.Equal("standard", note.GetProperty("detail").GetString());
        Assert.True(DateTimeOffset.TryParse(note.GetProperty("generatedAt").GetString(), out _));
        Assert.True(DateTimeOffset.TryParse(note.GetProperty("editedAt").GetString(), out _));
    }

    [Fact]
    public void SessionPatientFixtureCarriesTheTranslation()
    {
        var patient = LoadFixture("session-patient.json").RootElement.GetProperty("result");

        Assert.Equal("en", patient.GetProperty("language").GetString());
        var translation = patient.GetProperty("translation");
        Assert.Equal("pl", translation.GetProperty("language").GetString());
        Assert.Contains("łokcia", translation.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReflectionGetFixtureCarriesTheSummaryAndTheThreeAnswers()
    {
        var result = LoadFixture("reflection-get.json").RootElement.GetProperty("result");

        Assert.False(string.IsNullOrEmpty(result.GetProperty("label").GetString()));
        Assert.False(string.IsNullOrEmpty(result.GetProperty("summary").GetProperty("text").GetString()));
        var reflection = result.GetProperty("reflection");
        foreach (var key in new[] { "happened", "learned", "next" })
        {
            Assert.False(string.IsNullOrEmpty(reflection.GetProperty(key).GetString()), key);
        }

        Assert.True(DateTimeOffset.TryParse(reflection.GetProperty("createdAt").GetString(), out _));
    }

    [Fact]
    public void ReflectionListAndUpdateFixturesAgreeOnTheAnswers()
    {
        var listed = LoadFixture("reflection-list.json").RootElement
            .GetProperty("result").GetProperty("reflections")[0];
        var update = LoadFixture("reflection-update.json").RootElement.GetProperty("params");

        Assert.Equal(update.GetProperty("id").GetString(), listed.GetProperty("id").GetString());
        Assert.Equal(update.GetProperty("learned").GetString(), listed.GetProperty("learned").GetString());
        Assert.True(DateTimeOffset.TryParse(listed.GetProperty("startedAt").GetString(), out _));
        var summary = LoadFixture("reflection-summary.json").RootElement;
        Assert.Equal("reflection/summary", summary.GetProperty("method").GetString());
        Assert.False(summary.TryGetProperty("id", out _), "a notification, not a request");
    }

    [Fact]
    public void SessionLabelFixtureIsARequestWithIdAndText()
    {
        var root = LoadFixture("session-label.json").RootElement;

        Assert.Equal("session/label", root.GetProperty("method").GetString());
        Assert.Equal("Elbow swelling", root.GetProperty("params").GetProperty("text").GetString());
    }

    [Fact]
    public void SessionOpenAndCloseFixturesAreRequests()
    {
        var open = LoadFixture("session-open.json").RootElement;
        Assert.Equal("session/open", open.GetProperty("method").GetString());
        Assert.False(string.IsNullOrEmpty(open.GetProperty("params").GetProperty("id").GetString()));

        var close = LoadFixture("session-close.json").RootElement;
        Assert.Equal("session/close", close.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, close.GetProperty("params").ValueKind);
    }

    [Fact]
    public void ErrorFixtureCarriesReservedCode()
    {
        var error = LoadFixture("error-method-not-found.json").RootElement.GetProperty("error");
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void GuidanceSearchFixtureIsARequestWithAnIdAndALimit()
    {
        var root = LoadFixture("guidance-search.json").RootElement;

        Assert.Equal("guidance/search", root.GetProperty("method").GetString());
        Assert.True(root.TryGetProperty("id", out _), "a request, not a notification");
        Assert.False(
            string.IsNullOrEmpty(root.GetProperty("params").GetProperty("id").GetString()));
        Assert.Equal(3, root.GetProperty("params").GetProperty("limit").GetInt32());
    }

    [Fact]
    public void GuidanceReadyFixtureCarriesTheRecordAndBothResultShapes()
    {
        var root = LoadFixture("guidance-ready.json").RootElement;
        Assert.Equal("guidance/ready", root.GetProperty("method").GetString());
        var record = root.GetProperty("params");

        Assert.False(string.IsNullOrEmpty(record.GetProperty("id").GetString()));
        Assert.Equal(JsonValueKind.Null, record.GetProperty("storeError").ValueKind);
        Assert.Equal(JsonValueKind.False, record.GetProperty("stale").ValueKind);
        Assert.Equal(1, record.GetProperty("version").GetInt32());
        Assert.True(record.GetProperty("noteRevision").GetInt64() > 0);
        Assert.Equal(0.85, record.GetProperty("floor").GetDouble());
        Assert.Equal(JsonValueKind.False, record.GetProperty("abstained").ValueKind);

        var shown = record.GetProperty("shown");
        Assert.Equal(2, shown.GetArrayLength());
        var structured = shown[0];
        foreach (var key in new[]
                 {
                     "corpus", "chunkId", "code", "number", "title", "section", "text", "url",
                     "lastUpdated", "updateTag", "source", "citation", "trigger",
                 })
        {
            Assert.Equal(JsonValueKind.String, structured.GetProperty(key).ValueKind);
        }

        Assert.StartsWith(
            "https://", structured.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.True(DateOnly.TryParse(structured.GetProperty("lastUpdated").GetString(), out _));
        Assert.InRange(structured.GetProperty("score").GetDouble(), 0, 1);

        // A plain-text corpus: no section, date or tag, a file name for a link, the note as a whole
        var plain = shown[1];
        Assert.Equal("text", plain.GetProperty("source").GetString());
        foreach (var key in new[] { "section", "lastUpdated", "updateTag", "trigger" })
        {
            Assert.Equal("", plain.GetProperty(key).GetString());
        }

        Assert.False(Uri.TryCreate(plain.GetProperty("url").GetString(), UriKind.Absolute, out _));

        var searched = record.GetProperty("searched");
        Assert.Equal(2, searched.GetArrayLength());
        Assert.Equal(64, searched[0].GetProperty("sha256").GetString()!.Length);
        Assert.Equal(JsonValueKind.Null, searched[0].GetProperty("unavailable").ValueKind);
    }

    [Fact]
    public void GuidanceFailedFixtureNamesTheSessionAndTheReason()
    {
        var root = LoadFixture("guidance-failed.json").RootElement;

        Assert.Equal("guidance/failed", root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("id", out _), "a notification, not a request");
        Assert.False(
            string.IsNullOrEmpty(root.GetProperty("params").GetProperty("id").GetString()));
        Assert.False(
            string.IsNullOrEmpty(root.GetProperty("params").GetProperty("detail").GetString()));
    }

    [Fact]
    public void GuidanceCorporaFixtureCarriesTheStateAndBothCorpusShapes()
    {
        var result = LoadFixture("guidance-corpora.json").RootElement.GetProperty("result");

        Assert.Equal("ready", result.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("detail").ValueKind);
        var corpora = result.GetProperty("corpora");
        Assert.Equal(2, corpora.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, corpora[0].GetProperty("unavailable").ValueKind);
        foreach (var key in new[] { "id", "name", "licence", "attribution" })
        {
            Assert.False(string.IsNullOrEmpty(corpora[0].GetProperty(key).GetString()), key);
        }

        Assert.True(corpora[0].GetProperty("chunks").GetInt32() > 0);
        Assert.True(DateTimeOffset.TryParse(corpora[0].GetProperty("builtAt").GetString(), out _));
        Assert.False(string.IsNullOrEmpty(corpora[1].GetProperty("unavailable").GetString()));
        Assert.Equal("", corpora[1].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, corpora[1].GetProperty("builtAt").ValueKind);
    }

    [Fact]
    public void GuidanceModelFixtureIsANotificationWithStateAndDetail()
    {
        var root = LoadFixture("guidance-model.json").RootElement;

        Assert.Equal("guidance/model", root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("id", out _), "a notification, not a request");
        Assert.Equal("unavailable", root.GetProperty("params").GetProperty("state").GetString());
        Assert.False(
            string.IsNullOrEmpty(root.GetProperty("params").GetProperty("detail").GetString()));
    }

    [Fact]
    public void GuidanceDocumentsFixturesCarryARowInEveryState()
    {
        var listing = LoadFixture("guidance-documents.json").RootElement.GetProperty("result");
        var documents = listing.GetProperty("documents");

        Assert.False(string.IsNullOrEmpty(listing.GetProperty("folder").GetString()));
        Assert.True(listing.GetProperty("found").GetBoolean());
        Assert.True(listing.GetProperty("unsupported").GetInt32() >= 0);
        Assert.Equal(["ready", "indexing", "failed"],
            documents.EnumerateArray().Select(d => d.GetProperty("state").GetString()));
        foreach (var document in documents.EnumerateArray())
        {
            Assert.True(document.GetProperty("id").GetInt64() > 0);
            Assert.False(string.IsNullOrEmpty(document.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(document.GetProperty("path").GetString()));
            Assert.Equal(64, document.GetProperty("sha256").GetString()!.Length);
            Assert.True(
                DateTimeOffset.TryParse(document.GetProperty("addedAt").GetString(), out _));
            Assert.True(document.GetProperty("bytes").GetInt64() > 0);
        }

        Assert.True(documents[0].GetProperty("chunks").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Null, documents[0].GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, documents[1].GetProperty("indexedAt").ValueKind);
        Assert.Equal("patientData", documents[2].GetProperty("error").GetString());

        var add = LoadFixture("guidance-documents-add.json").RootElement.GetProperty("result");
        Assert.Equal(1, add.GetProperty("documents").GetArrayLength());
        Assert.Equal(["unsupported", "unreadable"], add.GetProperty("skipped").EnumerateArray()
            .Select(s => s.GetProperty("reason").GetString()));
    }

    [Fact]
    public void GuidancePageAndOpenFixturesCarryAPathAndTheBoxes()
    {
        var page = LoadFixture("guidance-page.json").RootElement.GetProperty("result");
        Assert.EndsWith(".bmp", page.GetProperty("path").GetString());
        Assert.True(page.GetProperty("width").GetInt32() > 0);
        Assert.True(page.GetProperty("height").GetInt32() > 0);
        Assert.True(page.GetProperty("pages").GetInt32() > 0);
        foreach (var box in page.GetProperty("boxes").EnumerateArray())
        {
            Assert.True(box.GetProperty("page").GetInt32() < page.GetProperty("pages").GetInt32());
            Assert.True(box.GetProperty("left").GetDouble() < box.GetProperty("right").GetDouble());
            Assert.True(box.GetProperty("top").GetDouble() < box.GetProperty("bottom").GetDouble());
            Assert.True(box.GetProperty("bottom").GetDouble() <= 1);
        }

        var opened = LoadFixture("guidance-documents-open.json").RootElement.GetProperty("result");
        Assert.False(string.IsNullOrEmpty(opened.GetProperty("path").GetString()));

        var shown = LoadFixture("guidance-ready.json").RootElement.GetProperty("params")
            .GetProperty("shown");
        foreach (var result in shown.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Number, result.GetProperty("pages").ValueKind);
        }
    }

    [Fact]
    public void GuidanceDocumentNotificationsCarryTheirParams()
    {
        var document = LoadFixture("guidance-document.json").RootElement;
        Assert.Equal("guidance/document", document.GetProperty("method").GetString());
        Assert.False(document.TryGetProperty("id", out _), "a notification, not a request");
        Assert.Equal("ready", document.GetProperty("params").GetProperty("state").GetString());

        var progress = LoadFixture("guidance-progress.json").RootElement.GetProperty("params");
        Assert.Equal("preparing", progress.GetProperty("phase").GetString());
        Assert.True(
            progress.GetProperty("done").GetInt32() <= progress.GetProperty("total").GetInt32());

        var changed = LoadFixture("guidance-documentsChanged.json").RootElement;
        Assert.Equal("guidance/documentsChanged", changed.GetProperty("method").GetString());
        Assert.Empty(changed.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public void SessionGuidanceFixtureCarriesTheStoredRecord()
    {
        var guidance = LoadFixture("session-guidance.json").RootElement
            .GetProperty("result").GetProperty("guidance");

        Assert.True(
            DateTimeOffset.TryParse(guidance.GetProperty("generatedAt").GetString(), out _));
        Assert.Equal(JsonValueKind.False, guidance.GetProperty("stale").ValueKind);
        Assert.Equal(1, guidance.GetProperty("version").GetInt32());
        Assert.True(guidance.GetProperty("noteRevision").GetInt64() > 0);
        Assert.Equal(1, guidance.GetProperty("shown").GetArrayLength());
        Assert.Equal(1, guidance.GetProperty("searched").GetArrayLength());
        // The stored record is the ready payload minus what only the wire carries
        Assert.False(guidance.TryGetProperty("id", out _));
        Assert.False(guidance.TryGetProperty("storeError", out _));
    }
}
