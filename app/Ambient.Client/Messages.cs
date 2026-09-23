using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Ambient.Client;

public sealed record PeerInfo(string Name, string Version, int ProtocolVersion);

public static class Protocol
{
    public const int ProtocolVersion = 1;

    // Non-ASCII patient and drug names go on the wire as readable UTF-8 without \uXXXX escapes
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>An object payload as a record, or null for anything else or a field of the wrong kind.</summary>
    public static T? Parse<T>(JsonElement element)
        where T : class
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return element.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A JSON-RPC error response from the engine.</summary>
public sealed class EngineErrorException(int code, string message, JsonElement? data)
    : Exception(message)
{
    public int Code { get; } = code;

    public JsonElement? ErrorData { get; } = data;
}
