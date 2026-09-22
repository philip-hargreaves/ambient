using System.Text.Json;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Features.Guidance;

public class PageViewModelTest
{
    private static GuidanceRecommendation Found(int page = 1, int pages = 5) =>
        GuidanceRecommendation.From(JsonSerializer.SerializeToElement(new
        {
            corpus = "upload:7",
            chunkId = "upload:7-4",
            code = "",
            number = "1.2",
            title = "BSR PMR guidelines 2009",
            section = "",
            text = "Start prednisolone 15 mg daily.",
            url = "",
            lastUpdated = "2026-09-15T09:12:44Z",
            updateTag = "",
            source = "upload",
            citation = "BSR PMR guidelines 2009, page 2, 1.2 (added 15 Sep 2026)",
            score = 0.9,
            trigger = "",
            document = 7L,
            page,
            pages,
        }), "", true);

    private static object Box(int page, double left, double top, double right, double bottom) =>
        new { page, left, top, right, bottom };

    private static object Reply(params object[] boxes) =>
        new { path = @"C:\scratch\page-7-1.bmp", width = 1000, height = 1400, pages = 5, boxes };

    private static (PageViewModel View, FakeEngineClient Engine) Create(object? reply)
    {
        var engine = new FakeEngineClient { PageReply = reply };
        var view = new PageViewModel
        {
            Request = (method, parameters) =>
                engine.RequestAsync(method, parameters, TimeSpan.FromSeconds(1)),
        };
        return (view, engine);
    }

    [Fact]
    public async Task ThePageViewClosesOnceItsPassageIsNoLongerACard()
    {
        var (view, _) = Create(Reply(Box(1, 0.1, 0.2, 0.6, 0.3)));
        await view.ShowAsync(Found());

        view.KeepOnlyFor([new GuidanceCard([Found()])]);
        Assert.True(view.Visible);

        view.KeepOnlyFor([]);
        Assert.False(view.Visible);
    }

    [Fact]
    public async Task ShowingAPassageAsksForItsPageAndScalesTheBoxes()
    {
        var (view, engine) = Create(Reply(Box(1, 0.1, 0.2, 0.6, 0.3)));

        await view.ShowAsync(Found());

        Assert.True(view.Visible);
        Assert.False(view.Loading);
        Assert.False(view.Failed);
        Assert.Equal("BSR PMR guidelines 2009", view.DocumentName);
        Assert.Equal("Page 2 of 5", view.PageLabel);
        Assert.Equal(@"C:\scratch\page-7-1.bmp", view.ImagePath);
        Assert.Equal(1000, view.Width);
        Assert.Equal(1400, view.Height);
        var box = Assert.Single(view.Boxes);
        Assert.Equal(100, box.Left, 3);
        Assert.Equal(280, box.Top, 3);
        Assert.Equal(500, box.Width, 3);
        Assert.Equal(140, box.Height, 3);
        Assert.NotNull(view.Focus);
        Assert.Equal(100, view.Focus.Left, 3);
        Assert.Equal(500, view.Focus.Width, 3);
        Assert.True(view.CanGoBack);
        Assert.True(view.CanGoForward);
        Assert.False(view.OffPassage);
        Assert.Equal("Page 2 of BSR PMR guidelines 2009, 1.2 highlighted", view.ImageName);
        var request = Assert.Single(engine.Requests, r => r.Method == "guidance/page");
        Assert.Equal("{\"id\":7,\"page\":1,\"chunkId\":\"upload:7-4\"}", request.Params);
    }

    [Fact]
    public async Task ThePagesTurnOneAtATimeAndTheMarksFollowThePassage()
    {
        var (view, engine) =
            Create(Reply(Box(1, 0.1, 0.8, 0.9, 0.95), Box(2, 0.1, 0.05, 0.9, 0.2)));

        await view.ShowAsync(Found());

        Assert.Equal("Page 2 of 5", view.PageLabel);
        Assert.Equal(0.8 * 1400, Assert.Single(view.Boxes).Top, 3);

        await view.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, engine.Requests.Count(r => r.Method == "guidance/page"));
        Assert.Contains("\"page\":2", engine.Requests[^1].Params);
        Assert.Equal("Page 3 of 5", view.PageLabel);
        Assert.Equal(0.05 * 1400, Assert.Single(view.Boxes).Top, 3);
        Assert.True(view.OffPassage);

        await view.NextPageCommand.ExecuteAsync(null);

        Assert.Equal("Page 4 of 5", view.PageLabel);
        Assert.Empty(view.Boxes);
        Assert.Null(view.Focus);
        Assert.Equal("Page 4 of BSR PMR guidelines 2009", view.ImageName);

        await view.NextPageCommand.ExecuteAsync(null);

        Assert.Equal("Page 5 of 5", view.PageLabel);
        Assert.False(view.CanGoForward);
        Assert.False(view.NextPageCommand.CanExecute(null));

        await view.BackToPassageCommand.ExecuteAsync(null);

        Assert.Equal("Page 2 of 5", view.PageLabel);
        Assert.False(view.OffPassage);
        Assert.Single(view.Boxes);
    }

    [Fact]
    public async Task TheFirstPageHasNoPrevious()
    {
        var (view, _) = Create(Reply(Box(0, 0.1, 0.2, 0.6, 0.3)));

        await view.ShowAsync(Found(page: 0));

        Assert.False(view.CanGoBack);
        Assert.False(view.PreviousPageCommand.CanExecute(null));
        Assert.True(view.CanGoForward);
    }

    [Fact]
    public async Task AFailedDrawSaysSoAndTryAgainAsksAgain()
    {
        var (view, engine) = Create(Reply(Box(1, 0.1, 0.2, 0.6, 0.3)));
        engine.FailNext = method =>
            method == "guidance/page" ? new InvalidOperationException("host crashed") : null;

        await view.ShowAsync(Found());

        Assert.True(view.Visible);
        Assert.True(view.Failed);
        Assert.False(view.Loading);
        Assert.Empty(view.Boxes);

        await view.TryAgainCommand.ExecuteAsync(null);

        Assert.False(view.Failed);
        Assert.Single(view.Boxes);
        Assert.Equal(2, engine.Requests.Count(r => r.Method == "guidance/page"));
    }

    [Fact]
    public async Task OpenAsksForTheCopyAndHandsThePathToTheViewer()
    {
        var (view, engine) = Create(null);
        engine.OpenedPath = @"C:\scratch\7.pdf";
        string? opened = null;
        view.OpenFile = path =>
        {
            opened = path;
            return Task.CompletedTask;
        };

        await view.OpenAsync(Found());

        Assert.Equal(@"C:\scratch\7.pdf", opened);
        var request = Assert.Single(engine.Requests, r => r.Method == "guidance/documents/open");
        Assert.Equal("{\"id\":7}", request.Params);
    }

    [Fact]
    public async Task CloseHidesThePaneAndCopyHandsTheCitationOn()
    {
        var (view, _) = Create(Reply());
        string? copied = null;
        view.CopyText = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        await view.ShowAsync(Found());

        await view.CopyCitationCommand.ExecuteAsync(null);
        view.CloseCommand.Execute(null);

        Assert.Equal("BSR PMR guidelines 2009, page 2, 1.2 (added 15 Sep 2026)", copied);
        Assert.False(view.Visible);
    }
}
