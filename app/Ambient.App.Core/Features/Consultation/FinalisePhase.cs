namespace Ambient.App.Core.Features.Consultation;

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
    Turns,  // the per-turn re-decode: transcript work again, long on the NPU
    Note,
    Streaming,
}
