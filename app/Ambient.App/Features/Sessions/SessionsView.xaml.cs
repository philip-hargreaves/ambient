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
    // Below this width everything goes into one selector
    private const double WideThreshold = 1100;

    // The narrow selector's views, in the order they were added
    private const int NarrowGuidelines = 2;
    private const int NarrowTranscript = 3;
    private const int NarrowPage = 4;

    // The reference selector's views
    private const int Guidelines = 0;
    private const int Transcript = 1;
    private const int Page = 2;

    private readonly TranscriptPaneView _transcript;
    private readonly NoteEditorView _note;
    private readonly PatientEditorView _patient;
    private readonly GuidanceSectionView _guidance;
    private readonly PageView _page;
    private readonly PageViewModel _pageView;
    private readonly ContentControl[] _narrow = [Slot(), Slot(), Slot(), Slot(), Slot()];
    private readonly ContentControl[] _documents = [Slot(), Slot()];
    private readonly ContentControl[] _references = [Slot(), Slot(), Slot()];
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
        _guidance = note.Guidance;
        _page = page;
        _pageView = consultation.PageView;
        InitializeComponent();
        // The selectors name the sections, so the views drop their own headings
        _note.PlaceGuidance(below: false);
        _guidance.ShowHeading(false);
        _transcript.ShowHeading(false);

        NarrowTabs.Add("Clinical note", _narrow[0]);
        NarrowTabs.Add("Patient information", _narrow[1]);
        NarrowTabs.Add("Guidelines", _narrow[2]);
        NarrowTabs.Add("Transcript", _narrow[3]);
        NarrowTabs.Add("Document page", _narrow[4]);
        DocumentTabs.Add("Clinical note", _documents[0]);
        DocumentTabs.Add("Patient information", _documents[1]);
        ReferenceTabs.Add("Guidelines", _references[0]);
        ReferenceTabs.Add("Transcript", _references[1]);
        ReferenceTabs.Add("Document page", _references[2]);
        Place(wide: false);
        PlaceGuidelines();
        PlacePage();

        _pageView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageViewModel.Visible))
            {
                PlacePage();
            }
        };
        // A page shown while another is open comes to the front too
        _pageView.Shown += PlacePage;
        _guidance.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidanceViewModel.Visible))
            {
                PlaceGuidelines();
            }
        };
        // A consultation opens on its note, with the guidance beside it when there is any
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionsViewModel.DetailOpen) && ViewModel.DetailOpen)
            {
                NarrowTabs.SelectedIndex = 0;
                DocumentTabs.SelectedIndex = 0;
                ReferenceTabs.SelectedIndex = _guidance.ViewModel.Visible ? Guidelines : Transcript;
            }
        };
        foreach (var tabs in new[] { NarrowTabs, DocumentTabs })
        {
            tabs.Loaded += (_, _) => Fit();
            tabs.SizeChanged += (_, _) => Fit();
        }
        Loaded += (_, _) => _ = ViewModel.EnterAsync();
    }

    public SessionsViewModel ViewModel { get; }

    private static ContentControl Slot() => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    public ShellViewModel Shell { get; }

    private void OnDetailSizeChanged(object sender, SizeChangedEventArgs e) =>
        Place(e.NewSize.Width >= WideThreshold);

    // One set of views, moved between the layouts' slots
    private void Place(bool wide)
    {
        if (_wide == wide)
        {
            return;
        }

        _wide = wide;
        foreach (var slot in _narrow.Concat(_documents).Concat(_references))
        {
            slot.Content = null;
        }
        if (wide)
        {
            _documents[0].Content = _note;
            _documents[1].Content = _patient;
            _references[Guidelines].Content = _guidance;
            _references[Transcript].Content = _transcript;
            _references[Page].Content = _page;
        }
        else
        {
            _narrow[0].Content = _note;
            _narrow[1].Content = _patient;
            _narrow[NarrowGuidelines].Content = _guidance;
            _narrow[NarrowTranscript].Content = _transcript;
            _narrow[NarrowPage].Content = _page;
        }

        WideLayout.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        NarrowLayout.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        Fit();
    }

    // The Guidelines tab exists while the section has something to say
    private void PlaceGuidelines()
    {
        var shown = _guidance.ViewModel.Visible;
        NarrowTabs.SetVisible(NarrowGuidelines, shown);
        ReferenceTabs.SetVisible(Guidelines, shown);
    }

    // An opened page is a tab that exists while it is open, and comes to the front
    private void PlacePage()
    {
        var open = _pageView.Visible;
        NarrowTabs.SetVisible(NarrowPage, open);
        ReferenceTabs.SetVisible(Page, open);
        if (open)
        {
            NarrowTabs.SelectedIndex = NarrowPage;
            ReferenceTabs.SelectedIndex = Page;
        }
    }

    private void Fit()
    {
        var area = _wide == true ? DocumentTabs.ContentArea : NarrowTabs.ContentArea;
        _note.FitTabContent(area);
        _patient.FitTabContent(area);
    }

    // The box binds as the text changes, so the view model is current when focus leaves
    private async void OnTitleCommitted(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync();
}
