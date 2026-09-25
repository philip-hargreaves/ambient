namespace Ambient.App.Core.Metrics;

/// <summary>
/// The machine's power situation at a moment: the Windows power-mode slider and mains
/// vs battery. Recorded with each session because the mode moves the finalise time.
/// </summary>
public sealed record PowerState(string Mode, bool OnMains)
{
    /// <summary>What a collector without a reader reports.</summary>
    public static PowerState Unknown { get; } = new("unknown", true);

    // The overlay GUIDs are fixed across Windows 10/11
    public static string ModeName(string overlayGuid) => overlayGuid.ToLowerInvariant() switch
    {
        "961cc777-2547-4f9d-8174-7d86181b8a7a" => "efficiency",
        "ded574b5-45a0-4f42-8737-46345c09c238" => "performance",
        "00000000-0000-0000-0000-000000000000" or "" => "balanced",
        _ => "unknown",
    };
}
