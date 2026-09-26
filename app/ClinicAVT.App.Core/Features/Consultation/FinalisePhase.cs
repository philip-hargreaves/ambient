namespace ClinicAVT.App.Core.Features.Consultation;

/// <summary>
/// The stages of a stop, in order. The engine reports Transcript and
/// Speakers as it starts them. The shell sets Note once the sealed transcript
/// has loaded and Streaming on the first note token.
/// </summary>
public enum FinalisePhase
{
    None,
    Sealing,
    Transcript,
    Speakers,
    Turns, // Per-turn re-decode. Transcript work again, and slow on the NPU
    Note,
    Streaming,
}
