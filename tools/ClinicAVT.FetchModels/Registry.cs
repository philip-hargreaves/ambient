using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicAVT.FetchModels;

/// <summary>One release asset. A large file is split across several.</summary>
public sealed record Shard(string Name, long Size, string Sha256);

/// <summary>
/// One downloadable pack, either a model directory or the demo tracks. Files
/// maps each installed file to its SHA-256. Shards lists the assets that
/// reassemble into each file, one shard when the file fits an asset whole.
/// Manifest, when present, becomes the store manifest.json with file sizes added.
/// </summary>
public sealed record Pack(
    string Id,
    string BaseUrl,
    string Target,
    Dictionary<string, string> Files,
    Dictionary<string, List<Shard>> Shards,
    JsonElement? Manifest)
{
    public static Pack Load(string path)
    {
        var pack = JsonSerializer.Deserialize<Pack>(File.ReadAllText(path), Json.Options)
            ?? throw new InvalidDataException($"empty registry entry: {path}");
        if (pack.Files.Count == 0 || pack.Shards.Count != pack.Files.Count)
        {
            throw new InvalidDataException($"registry entry out of shape: {path}");
        }

        return pack;
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json.Options));

    public long TotalBytes => Shards.Values.Sum(shards => shards.Sum(s => s.Size));
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
