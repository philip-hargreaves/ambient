namespace Ambient.App.Core.Features.Documents;

/// <summary>One transcript row. Live turns carry no speaker until the seal.</summary>
public sealed record TranscriptTurnItem(string Speaker, string TimeLabel, string Text)
{
    /// <summary>"Doctor", "Patient", an ellipsis while unknown, or the role as the engine named it.</summary>
    public string SpeakerLabel => Speaker switch
    {
        "doctor" => "Doctor",
        "patient" => "Patient",
        "" => "\u2026",
        _ => Speaker,
    };
}
