using System.Text.Json;
using Ambient.App.Core.Features.Guidance;
using Ambient.Client;

namespace Ambient.App.Tests.Support;

/// <summary>Wire-shaped guidance payloads applied straight to the view model.</summary>
internal static class GuidanceRecords
{
    public static void ApplyReady(this GuidanceViewModel guidance, JsonElement record) =>
        guidance.ApplyReady(Protocol.Parse<GuidanceRecord>(record)!);
}
