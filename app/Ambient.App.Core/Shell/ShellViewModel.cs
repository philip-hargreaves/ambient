using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Shell;

/// <summary>The routes a page offers beyond the navigation pane.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public ShellViewModel(INavigationService navigation) => _navigation = navigation;

    [RelayCommand]
    private void ShowSettings() => _navigation.NavigateTo("settings");
}
