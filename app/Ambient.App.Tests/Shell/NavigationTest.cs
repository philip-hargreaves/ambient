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
}
