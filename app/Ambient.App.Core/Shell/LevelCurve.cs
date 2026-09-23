namespace Ambient.App.Core.Shell;

/// <summary>
/// How the level ring answers the microphone: a soft glow that brightens and reaches further
/// with the level, one hairline ring that eases outward. Restrained on purpose, so it reads
/// as alive without alarming.
/// </summary>
public static class LevelCurve
{
    private const double GlowReach = 0.6;    // the glow's diameter beyond the disc at full level, as a fraction
    private const double RingReach = 0.22;   // the ring's swell at full level
    private const double GlowFloor = 0.08;   // glow alpha at silence
    private const double GlowCeiling = 0.42;
    private const double RingFloor = 0.25;   // ring alpha at silence
    private const double RingCeiling = 0.85;

    public static double GlowScale(double level) => 1.0 + GlowReach * Math.Clamp(level, 0.0, 1.0);

    public static double RingScale(double level) => 1.0 + RingReach * Math.Clamp(level, 0.0, 1.0);

    public static double GlowAlpha(double level) =>
        GlowFloor + (GlowCeiling - GlowFloor) * Math.Clamp(level, 0.0, 1.0);

    public static double RingAlpha(double level) =>
        RingFloor + (RingCeiling - RingFloor) * Math.Clamp(level, 0.0, 1.0);
}
