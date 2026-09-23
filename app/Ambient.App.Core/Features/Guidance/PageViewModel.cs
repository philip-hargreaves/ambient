using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Guidance;

/// <summary>One line of the passage on the drawn page, in pixels of the bitmap.</summary>
public sealed record PageBox(double Left, double Top, double Width, double Height);

/// <summary>
/// The page view beside the note: a page of an added document, opened on the cited
/// passage with its lines marked and turnable from there.
/// </summary>
public sealed partial class PageViewModel(
    IEngineApi engine, ILauncher launcher, IClipboard clipboard, StatusBarViewModel status)
    : ObservableObject
{
    private static readonly TimeSpan SlowAfter = TimeSpan.FromMilliseconds(1500);

    private GuidanceRecommendation? _shown;
    private int _page;
    private int _pages;
    private List<(int Page, double Left, double Top, double Right, double Bottom)> _boxes = [];
    private int _load;

    [ObservableProperty]
    public partial bool Visible { get; private set; }

    [ObservableProperty]
    public partial string DocumentName { get; private set; } = "";

    /// <summary>"Page 2 of 5".</summary>
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

    /// <summary>The page has been turned away from the passage.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackToPassageCommand))]
    public partial bool OffPassage { get; private set; }

    public ObservableCollection<PageBox> Boxes { get; } = [];

    /// <summary>The page's marks as one region, for the view to scroll to.</summary>
    [ObservableProperty]
    public partial PageBox? Focus { get; private set; }

    public bool SheetVisible => !Loading && !Failed;

    public bool PlaceholderVisible => Loading || Failed;

    public string ImageName => _shown is null ? ""
        : Boxes.Count == 0 ? $"Page {_page + 1} of {DocumentName}"
        : $"Page {_page + 1} of {DocumentName}, "
          + $"{(_shown.Number.Length > 0 ? _shown.Number : "the passage")} highlighted";

    public string Citation => _shown?.Citation ?? "";

    public bool CanGoBack => _page > 0;

    public bool CanGoForward => _page < _pages - 1;

    /// <summary>Opens on the passage's page and asks the engine to draw it.</summary>
    public async Task ShowAsync(GuidanceRecommendation found)
    {
        _shown = found;
        _page = found.Page;
        _pages = found.Pages;
        DocumentName = found.Title;
        Visible = true;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>A card's Open: the decrypted copy in the PDF viewer.</summary>
    public async Task OpenAsync(GuidanceRecommendation found)
    {
        try
        {
            var path = await engine.OpenDocumentAsync(found.Document).ConfigureAwait(true);
            if (path.Length > 0)
            {
                await launcher.OpenFileAsync(path).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            status.Append("The document could not be opened");
        }
    }

    /// <summary>Closes the view when the passage it shows is no longer among the cards.</summary>
    public void KeepOnlyFor(IEnumerable<GuidanceCard> cards)
    {
        if (Visible && _shown is not null
            && !cards.Any(card => card.Recommendations.Any(r => r.ChunkId == _shown.ChunkId)))
        {
            Hide();
        }
    }

    public void Hide()
    {
        Visible = false;
        _load++;
    }

    /// <summary>
    /// guidance/page: the bitmap, its size and the passage's boxes. The boxes come whole
    /// each time, so a page without any is a page the passage is not on.
    /// </summary>
    public void Apply(GuidancePage reply)
    {
        Width = reply.Width;
        Height = reply.Height;
        ImagePath = reply.Path ?? "";
        if (reply.Pages > 0)
        {
            _pages = reply.Pages;
        }

        _boxes = [];
        foreach (var box in reply.Boxes)
        {
            _boxes.Add((box.Page, box.Left, box.Top, box.Right, box.Bottom));
        }

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

        PageLabel = _pages > 0 ? $"Page {_page + 1} of {_pages}" : $"Page {_page + 1}";
        Loading = false;
        Slow = false;
        OnPropertyChanged(nameof(ImageName));
    }

    private async Task LoadAsync()
    {
        if (_shown is null)
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
        PageLabel = _pages > 0 ? $"Page {_page + 1} of {_pages}" : $"Page {_page + 1}";
        OffPassage = _page != _shown.Page;
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        _ = MarkSlowAsync(load);
        try
        {
            var reply = await engine.PageAsync(_shown.Document, _page, _shown.ChunkId)
                .ConfigureAwait(true);
            if (load == _load)
            {
                Apply(reply);
            }
        }
        catch (Exception e)
        {
            if (load == _load)
            {
                Failed = true;
                Loading = false;
                Slow = false;
                status.Append($"The page could not be shown: {e.Message}");
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
        _page--;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPage()
    {
        _page++;
        return LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(OffPassage))]
    private Task BackToPassage()
    {
        _page = _shown?.Page ?? _page;
        return LoadAsync();
    }

    [RelayCommand]
    private Task OpenDocument() => _shown is null ? Task.CompletedTask : OpenAsync(_shown);

    [RelayCommand]
    private Task CopyCitation() => clipboard.CopyAsync(status, Citation, "Citation");
}
