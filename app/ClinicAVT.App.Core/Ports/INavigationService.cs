namespace ClinicAVT.App.Core.Ports;

/// <summary>Navigation as a port so view models never touch Frame.</summary>
public interface INavigationService
{
    bool CanGoBack { get; }

    void NavigateTo(string pageKey);

    void GoBack();
}
