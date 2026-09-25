namespace Ambient.App.Core.Features.Guidance;

/// <summary>One line of the passage on the drawn page, in pixels of the bitmap.</summary>
public sealed record PageBox(double Left, double Top, double Width, double Height);
