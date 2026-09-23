using Microsoft.UI.Xaml;

namespace Ambient.App.Platform;

/// <summary>The main window, once launched, for adapters that need its handle or XAML root.</summary>
public sealed class WindowAccessor
{
    public Window? Window { get; set; }

    public XamlRoot? XamlRoot => (Window?.Content as FrameworkElement)?.XamlRoot;

    /// <summary>Unpackaged WinUI: a picker must be bound to the window handle.</summary>
    public bool BindToWindow(object picker)
    {
        if (Window is null)
        {
            return false;
        }

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(Window));
        return true;
    }
}
