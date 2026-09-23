using Microsoft.UI.Xaml;
using Ambient.App.Core.Ports;

namespace Ambient.App.Platform;

// ElementTheme.Default is follow-the-OS, so "system" tracks it live
public sealed class WinUiThemeService(WindowAccessor window) : IThemeService
{
    public void Apply(string theme)
    {
        if (window.Window?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}
