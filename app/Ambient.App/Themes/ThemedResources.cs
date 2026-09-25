using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ambient.App.Themes;

/// <summary>Resource lookup by element theme. A plain Application.Current.Resources
/// lookup follows the OS theme, not the theme the app has applied.</summary>
internal static class ThemedResources
{
    /// <summary>The value under the theme's dictionary, else the plain lookup, else null.</summary>
    public static object? Find(string key, ElementTheme theme)
    {
        // Dark is keyed "Default" in the dictionaries, as WinUI expects
        var light = theme == ElementTheme.Light
            || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Light);
        return Find(Application.Current.Resources, light ? "Light" : "Default", key)
            ?? (Application.Current.Resources.TryGetValue(key, out var plain) ? plain : null);
    }

    public static Brush GetBrush(string key, ElementTheme theme) =>
        Find(key, theme) as Brush ?? throw new KeyNotFoundException(key);

    private static object? Find(ResourceDictionary dictionary, string theme, string key)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
            && themed is ResourceDictionary resolved
            && resolved.TryGetValue(key, out var direct))
        {
            return direct;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, theme, key) is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }
}
