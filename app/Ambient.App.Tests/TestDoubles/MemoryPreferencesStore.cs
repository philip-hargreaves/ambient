using Ambient.App.Core.Ports;

namespace Ambient.App.Tests.TestDoubles;

internal sealed class MemoryPreferencesStore : IPreferencesStore
{
    public string? Json { get; set; }

    public string? Read() => Json;

    public void Write(string json) => Json = json;
}
