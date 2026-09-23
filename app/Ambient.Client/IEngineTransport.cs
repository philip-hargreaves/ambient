using System.Text.Json;

namespace Ambient.Client;

/// <summary>
/// The raw JSON-RPC link to the engine. PipeTransport is the production
/// implementation and tests use a fake. The shell speaks through IEngineApi.
/// </summary>
public interface IEngineTransport : IAsyncDisposable
{
    event Action<string, JsonElement>? NotificationReceived;

    /// <summary>True while a verified transport is up and requests can succeed.</summary>
    bool Connected { get; }

    event Action<bool>? ConnectedChanged;

    Task<JsonElement> RequestAsync(
        string method, object? parameters, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
