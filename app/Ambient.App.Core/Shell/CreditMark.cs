namespace Ambient.App.Core.Shell;

/// <summary>One partner mark. Dark falls back to the light artwork.</summary>
public sealed record CreditMark(string Name, string LightPath, string DarkPath, double Height);
