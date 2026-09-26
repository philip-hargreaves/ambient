namespace ClinicAVT.App.Core.Features.Guidance;

/// <summary>
/// Whether the engine's embedder has loaded. Nothing can be searched before it has.
/// </summary>
public enum GuidanceReadiness
{
    Loading,
    Ready,
    Unavailable,
}
