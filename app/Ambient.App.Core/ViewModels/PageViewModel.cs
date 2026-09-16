using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static Ambient.App.Core.ViewModels.GuidanceRecommendation;

namespace Ambient.App.Core.ViewModels;

/// <summary>One line of the passage on the drawn page, in pixels of the bitmap.</summary>
public sealed record PageBox(double Left, double Top, double Width, double Height);

/// <summary>
/// The page view beside the note: one page of an added document with the cited
/// passage's lines marked. The consultation view model supplies the engine calls,
/// the view supplies the file launcher and the clipboard.
/// </summary>
public sealed partial class PageViewModel : ObservableObject
{
    private static readonly TimeSpan SlowAfter = TimeSpan.FromMilliseconds(1500);

    private GuidanceRecommendation? _shown;
    private int _page;
    private List<int> _boxPages = [];
    private List<(int Page, double Left, double Top, double Right, double Bottom)> _boxes = [];
    private int _load;

    public Func<string, object, Task<JsonElement>>? Request { get; set; }

    public Func<string, Task>? OpenFile { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    public Action<string>? Report { get; set; }

    [ObservableProperty]
    public partial bool Visible { get; private set; }

    [ObservableProperty]
    public partial string DocumentName { get; private set; } = "";

    /// <summary>"Page 2 of 5", or "Pages 2-3" when the passage spans two.</summary>
    [ObservableProperty]
    public partial string PageLabel { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetVisible), nameof(PlaceholderVisible))]
    public partial bool Loading { get; private set; }

    /// <summary>Past a second and a half a caption says the page is still coming.</summary>
    [ObservableProperty]
    public partial bool Slow { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SheetVisible), nameof(PlaceholderVisible))]
    public partial bool Failed { get; private set; }

    [ObservableProperty]
    public partial string ImagePath { get; private set; } = "";

    [ObservableProperty]
    public partial double Width { get; private set; } = 595;

    [ObservableProperty]
    public partial double Height { get; private set; } = 842;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial bool Spans { get; private set; }

    public ObservableCollection<PageBox> Boxes { get; } = [];

    /// <summary>The page's marks as one region, for the view to scroll to.</summary>
    [ObservableProperty]
    public partial PageBox? Focus { get; private set; }

    public bool SheetVisible => !Loading && !Failed;

    public bool PlaceholderVisible => Loading || Failed;

    public string ImageName => _shown is null ? ""
        : $"Page {_page + 1} of {DocumentName}, "
          + $"{(_shown.Number.Length > 0 ? _shown.Number : "the passage")} highlighted";

    public string Citation => _shown?.Citation ?? "";

    public bool CanGoBack => Spans && _boxPages.IndexOf(_page) > 0;

    public bool CanGoForward => Spans && _boxPages.IndexOf(_page) < _boxPages.Count - 1;

    /// <summary>Opens on the passage's page and asks the engine to draw it.</summary>
    public async Task ShowAsync(GuidanceRecommendation found)
    {
        _shown = found;
        _page = found.Page;
        DocumentName = found.Title;
        Visible = true;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>A card's Open: the decrypted copy in the PDF viewer.</summary>
    public async Task OpenAsync(GuidanceRecommendation found)
    {
        if (Request is null || OpenFile is null)
        {
            return;
        }

        try
        {
            var reply = await Request("guidance/documents/open", new { id = found.Document })
                .ConfigureAwait(true);
            var path = Field(reply, "path");
            if (path.Length > 0)
            {
                await OpenFile(path).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            Report?.Invoke("The document could not be opened");
        }
    }

    public void Hide()
    {
        Visible = false;
        _load++;
    }

    /// <summary>guidance/page: the bitmap, its size and the passage's boxes.</summary>
    public void Apply(JsonElement reply)
    {
        Width = Numeric(reply, "width");
        Height = Numeric(reply, "height");
        ImagePath = Field(reply, "path");
        var pages = (int)Numeric(reply, "pages");
        _boxes = [];
        if (reply.TryGetProperty("boxes", out var boxes) && boxes.ValueKind == JsonValueKind.Array)
        {
            foreach (var box in boxes.EnumerateArray())
            {
                _boxes.Add(((int)Numeric(box, "page"), Numeric(box, "left"), Numeric(box, "top"),
                    Numeric(box, "right"), Numeric(box, "bottom")));
            }
        }

        _boxPages = _boxes.Select(b => b.Page).Distinct().Order().ToList();
        Boxes.Clear();
        var onPage = _boxes.Where(b => b.Page == _page).ToList();
        foreach (var box in onPage)
        {
            Boxes.Add(new PageBox(box.Left * Width, box.Top * Height,
                (box.Right - box.Left) * Width, (box.Bottom - box.Top) * Height));
        }

        Focus = onPage.Count == 0 ? null : new PageBox(
            onPage.Min(b => b.Left) * Width, onPage.Min(b => b.Top) * Height,
            (onPage.Max(b => b.Right) - onPage.Min(b => b.Left)) * Width,
            (onPage.Max(b => b.Bottom) - onPage.Min(b => b.Top)) * Height);

        Spans = _boxPages.Count > 1;
        PageLabel = Spans
            ? $"Pages {_boxPages[0] + 1}-{_boxPages[^1] + 1}"
            : pages > 0 ? $"Page {_page + 1} of {pages}" : $"Page {_page + 1}";
        Loading = false;
        Slow = false;
        OnPropertyChanged(nameof(ImageName));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        if (_shown is null || Request is null)
        {
            return;
        }

        var load = ++_load;
        Loading = true;
        Slow = false;
        Failed = false;
        Boxes.Clear();
        Focus = null;
        ImagePath = "";
        PageLabel = $"Page {_page + 1}";
        _ = MarkSlowAsync(load);
        try
        {
            var reply = await Request("guidance/page",
                new { id = _shown.Document, page = _page, chunkId = _shown.ChunkId })
                .ConfigureAwait(true);
            if (load == _load)
            {
                Apply(reply);
            }
        }
        catch (Exception)
        {
            if (load == _load)
            {
                Failed = true;
                Loading = false;
                Slow = false;
            }
        }
    }

    private async Task MarkSlowAsync(int load)
    {
        await Task.Delay(SlowAfter).ConfigureAwait(true);
        if (load == _load && Loading)
        {
            Slow = true;
        }
    }

    [RelayCommand]
    private void Close() => Hide();

    [RelayCommand]
    private Task TryAgain() => LoadAsync();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPage()
    {
        _page = _boxPages[_boxPages.IndexOf(_page) - 1];
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPage()
    {
        _page = _boxPages[_boxPages.IndexOf(_page) + 1];
        return LoadAsync();
    }

    [RelayCommand]
    private Task OpenDocument() => _shown is null ? Task.CompletedTask : OpenAsync(_shown);

    [RelayCommand]
    private Task CopyCitation() => CopyText?.Invoke(Citation) ?? Task.CompletedTask;
}
