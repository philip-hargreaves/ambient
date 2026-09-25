using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Shell;

/// <summary>Navigation between the pages, with what leaving one and arriving at another means.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly SessionsViewModel _sessions;
    private readonly AppraisalsViewModel _appraisals;
    private string? _current;

    public ShellViewModel(
        INavigationService navigation, SessionsViewModel sessions, AppraisalsViewModel appraisals)
    {
        _navigation = navigation;
        _sessions = sessions;
        _appraisals = appraisals;
    }

    /// <summary>
    /// Shows the page with that key. Leaving Appraisal saves whatever reflection is open;
    /// Consultation means record a new one, so a stored review ends before the page shows.
    /// </summary>
    [RelayCommand]
    public async Task NavigateAsync(string key)
    {
        if (_current == Routes.Appraisals && key != Routes.Appraisals)
        {
            await _appraisals.LeaveAsync().ConfigureAwait(true);
        }

        if (key == Routes.Consultation)
        {
            await _sessions.CloseStoredReviewAsync().ConfigureAwait(true);
        }

        _current = key;
        _navigation.NavigateTo(key);
    }

    [RelayCommand]
    private Task ShowSettings() => NavigateAsync(Routes.Settings);
}
