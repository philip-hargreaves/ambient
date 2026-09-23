namespace Ambient.App.Core.Ports;

/// <summary>Applies "system", "light" or "dark" to the window.</summary>
public interface IThemeService
{
    void Apply(string theme);
}
