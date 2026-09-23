using System.Text.Json;

namespace Ambient.App.Tests.Support;

/// <summary>Payloads as the wire carries them.</summary>
internal static class Wire
{
    public static JsonElement Params(object value) => JsonSerializer.SerializeToElement(value);
}
