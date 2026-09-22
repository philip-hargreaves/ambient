using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;


namespace Ambient.App.Tests.Shell;

public class NavigationTest
{
    [Fact]
    public void ShowSettingsNavigatesThroughThePort()
    {
        var navigation = new RecordingNavigationService();
        var shell = new ShellViewModel(navigation);

        shell.ShowSettingsCommand.Execute(null);

        Assert.Equal("settings", navigation.Current);
    }

    [Fact]
    public void ShowAppraisalsNavigatesThroughThePort()
    {
        var navigation = new RecordingNavigationService();
        var shell = new ShellViewModel(navigation);

        shell.ShowAppraisalsCommand.Execute(null);

        Assert.Equal("appraisals", navigation.Current);
    }

    [Fact]
    public void GoBackReturnsToThePreviousSurface()
    {
        var navigation = new RecordingNavigationService();
        navigation.NavigateTo("consultation");
        var shell = new ShellViewModel(navigation);

        shell.ShowSettingsCommand.Execute(null);
        shell.GoBackCommand.Execute(null);

        Assert.Equal("consultation", navigation.Current);
        Assert.False(navigation.CanGoBack);
    }
}
