using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Documents;

namespace Ambient.App.Features.Documents;

public sealed partial class PatientEditorView : UserControl
{
    // The sheet's share of the area: most of it alone, under half beside a translation
    private readonly TabFit _fitAlone;
    private readonly TabFit _fitShared;
    private FrameworkElement? _area;

    public PatientEditorView(NoteViewModel viewModel, DocumentExportViewModel export)
    {
        ViewModel = viewModel;
        Export = export;
        InitializeComponent();
        _fitAlone = new TabFit(PatientBox, 0.6);
        _fitShared = new TabFit(PatientBox, 0.42);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.PatientEditing))
            {
                EditingChrome.Show(PatientBox, ViewModel.PatientEditing);
            }

            if (e.PropertyName == nameof(NoteViewModel.TranslationVisible))
            {
                TranslationRow.Height = ViewModel.TranslationVisible ? GridLength.Auto : new GridLength(0);
                if (_area is not null)
                {
                    FitTabContent(_area);
                }
            }
        };
        PatientBox.SizeChanged += (_, _) => FitTranslation();
    }

    public NoteViewModel ViewModel { get; }

    public DocumentExportViewModel Export { get; }

    // Inside a tab the sheet keeps at most three fifths of the content area, or
    // under half when a translation shares it, leaving room for the actions
    public void FitTabContent(FrameworkElement area)
    {
        _area = area;
        (ViewModel.TranslationVisible ? _fitShared : _fitAlone).Fit(area);
        FitTranslation();
    }

    // The translation takes what the sheet and the rows around it leave, and scrolls
    // past that, so the actions sit right under it
    private void FitTranslation()
    {
        if (_area is null || _area.ActualHeight <= 0 || !ViewModel.TranslationVisible)
        {
            return;
        }

        var sheet = PatientBox.ActualHeight > 0
            ? Math.Min(PatientBox.ActualHeight, PatientBox.MaxHeight)
            : PatientBox.MaxHeight;
        var chrome = HeaderRow.ActualHeight + ActionRow.ActualHeight + TranslationCaption.ActualHeight
            + 3 * Editor.RowSpacing + 4 + Editor.Padding.Top + Editor.Padding.Bottom;
        TranslationBox.MaxHeight = Math.Max(TranslationBox.MinHeight, _area.ActualHeight - sheet - chrome);
    }
}
