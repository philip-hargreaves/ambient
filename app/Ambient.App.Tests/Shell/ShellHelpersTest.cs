using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Tests.Shell;

/// <summary>The pure helpers the views lean on.</summary>
public class ShellHelpersTest
{
    [Fact]
    public void TheLevelCurveIsClampedAndRestrained()
    {
        Assert.Equal(1.0, LevelCurve.GlowScale(0));
        Assert.Equal(1.6, LevelCurve.GlowScale(1));
        Assert.Equal(1.6, LevelCurve.GlowScale(4), 10);
        Assert.Equal(1.0, LevelCurve.RingScale(-1));
        Assert.Equal(0.08, LevelCurve.GlowAlpha(0), 10);
        Assert.Equal(0.42, LevelCurve.GlowAlpha(1), 10);
        Assert.Equal(0.25, LevelCurve.RingAlpha(0), 10);
        Assert.Equal(0.85, LevelCurve.RingAlpha(1), 10);
        Assert.True(LevelCurve.GlowAlpha(0.5) < LevelCurve.GlowAlpha(0.6));
    }

    [Theory]
    [InlineData(0, false, "No reflections")]
    [InlineData(3, true, "Show the whole year")]
    [InlineData(1, false, "1 reflection, press to show only this month")]
    [InlineData(4, false, "4 reflections, press to show only this month")]
    public void AMonthTipCountsAndPluralises(int count, bool selected, string expected) =>
        Assert.Equal(expected, new MonthMarker(3, "Mar", count, Selected: selected).Tip);

    [Theory]
    [InlineData("https://www.nice.org.uk/guidance/ng100", true)]
    [InlineData("http://example.test", true)]
    [InlineData("Gout.md", false)]
    [InlineData("file:///C:/notes.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void OnlyWebAddressesOpenInTheBrowser(string link, bool expected) =>
        Assert.Equal(expected, WebLinks.IsWeb(link));

    [Fact]
    public void NavigationHistoryGoesBackThroughWhatWasShown()
    {
        var history = new NavigationHistory<string>();
        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);

        history.Show("consultation");
        history.Show("settings");
        Assert.Equal("settings", history.Current);
        Assert.True(history.CanGoBack);

        Assert.Equal("consultation", history.Back());
        Assert.Equal("consultation", history.Current);
        Assert.False(history.CanGoBack);
        Assert.Null(history.Back());
        Assert.Equal("consultation", history.Current);
    }
}
