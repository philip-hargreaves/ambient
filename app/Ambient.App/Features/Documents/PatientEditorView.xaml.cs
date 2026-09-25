using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Documents;

namespace Ambient.App.Features.Documents;

public sealed partial class PatientEditorView : UserControl
{
    private readonly TabFit _fit;

    public PatientEditorView(NoteViewModel viewModel, DocumentExportViewModel export)
    {
        ViewModel = viewModel;
        Export = export;
        InitializeComponent();
        _fit = new TabFit(PatientBox, 0.6);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.PatientEditing))
            {
                EditingChrome.Show(PatientBox, ViewModel.PatientEditing);
            }

            if (e.PropertyName == nameof(NoteViewModel.TranslationVisible))
            {
                TranslationRow.Height = ViewModel.TranslationVisible
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
            }
        };
    }

    public NoteViewModel ViewModel { get; }

    public DocumentExportViewModel Export { get; }

    // Inside a tab the sheet keeps at most three fifths of the content area,
    // leaving room for the actions and a translation
    public void FitTabContent(FrameworkElement area) => _fit.Fit(area);
}
