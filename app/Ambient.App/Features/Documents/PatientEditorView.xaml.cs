using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Documents;

public sealed partial class PatientEditorView : UserControl
{
    private readonly StatusBarViewModel _status;
    private readonly IClipboard _clipboard;
    private readonly IFilePicker _picker;
    private readonly TabFit _fit;

    public PatientEditorView(
        NoteViewModel viewModel, StatusBarViewModel status, IClipboard clipboard, IFilePicker picker)
    {
        ViewModel = viewModel;
        _status = status;
        _clipboard = clipboard;
        _picker = picker;
        InitializeComponent();
        _fit = new TabFit(PatientBox, 0.6);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.PatientEditing))
            {
                ShowEditingChrome(PatientBox, ViewModel.PatientEditing);
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

    // Inside a tab the sheet keeps at most three fifths of the content area,
    // leaving room for the actions and a translation
    public void FitTabContent(TabView tabs) => _fit.Fit(tabs);

    public void FollowContent() => _fit.Follow();

    // The mode must be visible: an accent border while editing, the flat
    // document look otherwise
    private static void ShowEditingChrome(TextBox box, bool editing)
    {
        if (editing)
        {
            box.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
                Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
            box.BorderThickness = new Thickness(1.5);
        }
        else
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
        }
    }


    // The sheet and its translation travel together to the patient
    private async void OnExportPatient(object sender, RoutedEventArgs e)
    {
        var path = await _picker.PickSaveAsync("patient-sheet.txt", "Text file", ".txt");
        if (path is null)
        {
            return;
        }

        var text = DocumentExport.Marker + ViewModel.PatientInfoText;
        if (ViewModel.TranslationText.Length > 0)
        {
            text += "\n\n" + ViewModel.TranslationCaption + "\n\n" + ViewModel.TranslationText;
        }

        await System.IO.File.WriteAllTextAsync(path, text);
        _status.Append($"Saved to {System.IO.Path.GetFileName(path)} - outside the encrypted store");
    }

    private async void OnCopyPatient(object sender, RoutedEventArgs e) =>
        await _clipboard.CopyAsync(_status, ViewModel.PatientInfoText, "Patient note");
}
