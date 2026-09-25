namespace Ambient.App.Core.Features.Guidance;

/// <summary>Whether the engine's embedder has loaded, so it can search at all.</summary>
public enum GuidanceReadiness
{
    Loading,
    Ready,
    Unavailable,
}
