using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Platform;

/// <summary>
/// Swaps the host's content between the page views, keeping a back stack so returning
/// to a surface restores it. Pages are singletons, so a surface is built once and keeps
/// its state.
/// </summary>
public sealed class NavigationService(IReadOnlyDictionary<string, Func<UIElement>> pages)
    : INavigationService
{
    private readonly NavigationHistory<string> _stack = new();
    private ContentControl? _host;

    /// <summary>The page now on screen, by key, however it got there.</summary>
    public event Action<string>? Navigated;

    public bool CanGoBack => _stack.CanGoBack;

    public void Attach(ContentControl host) => _host = host;

    public void NavigateTo(string pageKey)
    {
        if (_stack.Current == pageKey)
        {
            return;
        }
        _stack.Show(pageKey);
        Show(pageKey);
    }

    public void GoBack()
    {
        if (_stack.Back() is { } previous)
        {
            Show(previous);
        }
    }

    private void Show(string pageKey)
    {
        Host().Content = pages[pageKey]();
        Navigated?.Invoke(pageKey);
    }

    private ContentControl Host() =>
        _host ?? throw new InvalidOperationException("navigation host not attached");
}
