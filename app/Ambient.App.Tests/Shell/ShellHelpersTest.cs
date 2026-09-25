using Ambient.App.Core.Common;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

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
    [InlineData("https://www.nice.org.uk/guidance/ng100", true)]
    [InlineData("http://example.test", true)]
    [InlineData("Gout.md", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void OnlyWebAddressesOpenInTheBrowser(string link, bool expected) =>
        Assert.Equal(expected, WebLinks.IsWeb(link));

    [Fact]
    public async Task NavigationGoesThroughThePortAndHistoryGoesBackThroughWhatWasShown()
    {
        var navigation = new RecordingNavigationService();
        var (shell, _, _, _) = Shell(navigation);
        await shell.ShowSettingsCommand.ExecuteAsync(null);
        Assert.Equal(Routes.Settings, navigation.Current);

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

    [Fact]
    public async Task GoingToRecordEndsAStoredReviewAndLeavingAppraisalClosesTheOpenReflection()
    {
        var navigation = new RecordingNavigationService();
        var (shell, consultation, sessions, appraisals) = Shell(navigation);
        var engine = consultation.Engine;

        engine.StoredNote = "the stored note";
        await consultation.Session.OpenStoredSessionAsync("abc");
        Assert.True(consultation.Session.ReviewingStored);

        await shell.NavigateCommand.ExecuteAsync(Routes.Sessions);
        Assert.True(consultation.Session.ReviewingStored);
        Assert.Equal(Routes.Sessions, navigation.Current);

        await shell.NavigateCommand.ExecuteAsync(Routes.Consultation);
        Assert.False(consultation.Session.ReviewingStored);
        Assert.Equal(Routes.Consultation, navigation.Current);

        engine.Reflections.Add(("r1", "2026-09-01T10:00:00Z", "", "listen longer", ""));
        await shell.NavigateCommand.ExecuteAsync(Routes.Appraisals);
        await appraisals.RefreshAsync();
        var card = Assert.Single(appraisals.Cards);
        await appraisals.ToggleAsync(card);
        Assert.True(card.Expanded);

        await shell.NavigateCommand.ExecuteAsync(Routes.Help);
        Assert.False(card.Expanded);
        Assert.Equal(Routes.Help, navigation.Current);
        Assert.Null(sessions.Selected);
    }

    private static (ShellViewModel Shell,
        (ConsultationViewModel Session, FakeEngineClient Engine) Consultation,
        SessionsViewModel Sessions, AppraisalsViewModel Appraisals) Shell(RecordingNavigationService navigation)
    {
        var (session, engine, _) = TestSession.Create();
        var api = new EngineApi(engine);
        var sessions = new SessionsViewModel(api, session.Status, session, new FakeDialogService());
        var appraisals = new AppraisalsViewModel(
            api, new InlineDispatcher(), session.Status, new FakeClipboard(), new FakeFilePicker(),
            new FakeDialogService());
        return (new ShellViewModel(navigation, sessions, appraisals), (session, engine), sessions, appraisals);
    }
}
