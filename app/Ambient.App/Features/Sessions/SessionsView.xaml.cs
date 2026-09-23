using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Documents;
using Ambient.App.Features.Guidance;

namespace Ambient.App.Features.Sessions;

public sealed partial class SessionsView : UserControl
{
    // Below this width the documents go into tabs
    private const double WideThreshold = 1100;

    private readonly TranscriptPaneView _transcript;
    private readonly NoteEditorView _note;
    private readonly PatientEditorView _patient;
    private readonly PageView _page;
    private readonly PageViewModel _pageView;
    private bool? _wide;

    public SessionsView(
        SessionsViewModel viewModel, ShellViewModel shell,
        TranscriptPaneView transcript, NoteEditorView note, PatientEditorView patient,
        ConsultationViewModel consultation, PageView page)
    {
        ViewModel = viewModel;
        Shell = shell;
        _transcript = transcript;
        _note = note;
        _patient = patient;
        _page = page;
        _pageView = consultation.PageView;
        InitializeComponent();
        Place(wide: false);
        _pageView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageViewModel.Visible))
            {
                PlacePage();
            }
        };
        NarrowTabs.Loaded += (_, _) => FitNarrow();
        NarrowTabs.SizeChanged += (_, _) => FitNarrow();
        Loaded += (_, _) => _ = ViewModel.RefreshAsync();
    }

    public SessionsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    private void OnDetailSizeChanged(object sender, SizeChangedEventArgs e) =>
        Place(e.NewSize.Width >= WideThreshold);

    // The document views are shared with the live screen, so they are moved, not duplicated
    private void Place(bool wide)
    {
        if (_wide == wide)
        {
            return;
        }

        _wide = wide;
        NoteHost.Content = null;
        PatientHost.Content = null;
        TranscriptHost.Content = null;
        PageHost.Content = null;
        NoteHostWide.Content = null;
        PatientHostWide.Content = null;
        TranscriptHostWide.Content = null;
        PageHostWide.Content = null;
        if (wide)
        {
            NoteHostWide.Content = _note;
            PatientHostWide.Content = _patient;
            TranscriptHostWide.Content = _transcript;
            PageHostWide.Content = _page;
        }
        else
        {
            NoteHost.Content = _note;
            PatientHost.Content = _patient;
            TranscriptHost.Content = _transcript;
            PageHost.Content = _page;
        }

        WideLayout.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        NarrowLayout.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        PlacePage();
        // The wide column scrolls as a page; the tabs bound the editors
        if (wide)
        {
            _note.FollowContent();
            _patient.FollowContent();
        }
        else
        {
            FitNarrow();
        }
    }

    // Wide, the page view takes the transcript's card. Narrow, it is a tab that
    // exists only while it is open
    private void PlacePage()
    {
        var open = _pageView.Visible;
        TranscriptCard.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        PageHostWide.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PageTab.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && _wide == false)
        {
            NarrowTabs.SelectedItem = PageTab;
        }
        else if (!open && ReferenceEquals(NarrowTabs.SelectedItem, PageTab))
        {
            NarrowTabs.SelectedIndex = 0;
        }
    }

    private void FitNarrow()
    {
        if (_wide == false)
        {
            _note.FitTabContent(NarrowTabs);
            _patient.FitTabContent(NarrowTabs);
        }
    }

    // The box binds as the text changes, so the view model is current when focus leaves
    private async void OnTitleCommitted(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync();
}
