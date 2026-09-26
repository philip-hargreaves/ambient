using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClinicAVT.App.Core.Ports;
using ClinicAVT.App.Core.Shell;

namespace ClinicAVT.App.Platform;

/// <summary>
/// Swaps the host's content between pages and keeps a back stack. Pages are singletons,
/// so each keeps its state between visits.
/// </summary>
public sealed class NavigationService(IReadOnlyDictionary<string, Func<UIElement>> pages)
    : INavigationService
{
    private readonly NavigationHistory<string> _stack = new();
    private ContentControl? _host;

    /// <summary>Raised with the page key on every navigation, forward or back.</summary>
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
