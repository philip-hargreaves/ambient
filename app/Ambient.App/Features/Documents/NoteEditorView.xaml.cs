using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Guidance;
using Ambient.App.Platform;

namespace Ambient.App.Features.Documents;

public sealed partial class NoteEditorView : UserControl
{
    private readonly StatusBarViewModel _status;
    private readonly TabFit _fit;

    public NoteEditorView(
        NoteViewModel viewModel, StatusBarViewModel status, GuidanceSectionView guidance)
    {
        ViewModel = viewModel;
        _status = status;
        InitializeComponent();
        Select(StyleBox, ViewModel.Style);
        Select(DetailBox, ViewModel.Detail);
        GuidanceHost.Content = guidance;
        _fit = new TabFit(NoteBox, 0.5);
        guidance.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidanceViewModel.Hovered))
            {
                LightSentence(guidance.ViewModel.Hovered);
            }
        };
        // A stored session brings its own options; the combos follow
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.NoteEditing))
            {
                ShowEditingChrome(NoteBox, ViewModel.NoteEditing);
            }
            else if (e.PropertyName == nameof(NoteViewModel.Style))
            {
                Select(StyleBox, ViewModel.Style);
            }
            else if (e.PropertyName == nameof(NoteViewModel.Detail))
            {
                Select(DetailBox, ViewModel.Detail);
            }
        };
    }

    public NoteViewModel ViewModel { get; }

    // Inside a tab the note keeps at most half of the content area and the
    // guidance row gets the rest
    public void FitTabContent(TabView tabs) => _fit.Fit(tabs);

    public void FollowContent() => _fit.Follow();

    // The hovered card's sentence, lit in the read-only note. An editor in
    // use keeps its own selection
    private void LightSentence(string sentence)
    {
        if (ViewModel.NoteEditing)
        {
            return;
        }

        var at = sentence.Length > 0
            ? NoteBox.Text.IndexOf(sentence, StringComparison.Ordinal)
            : -1;
        NoteBox.Select(Math.Max(at, 0), at < 0 ? 0 : sentence.Length);
    }

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


    // The view model speaks engine values, the combos display names; the
    // glue lives here with the values travelling as item tags
    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string?)i.Tag == tag) ?? box.Items[0];

    private void OnStyleChanged(object sender, SelectionChangedEventArgs args)
    {
        if (StyleBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            ViewModel.Style = tag;
        }
    }

    private void OnDetailChanged(object sender, SelectionChangedEventArgs args)
    {
        if (DetailBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            ViewModel.Detail = tag;
        }
    }

    // Export is the one action that writes outside the encrypted store
    private async void OnExportNote(object sender, RoutedEventArgs e)
    {
        var path = await SavePickerHelper.PickAsync("clinical-note.txt", "Text file", ".txt");
        if (path is null)
        {
            return;
        }

        await System.IO.File.WriteAllTextAsync(
            path, SavePickerHelper.ExportMarker + ViewModel.ClinicalNoteText);
        _status.Append($"Saved to {System.IO.Path.GetFileName(path)} - outside the encrypted store");
    }

    private async void OnCopyNote(object sender, RoutedEventArgs e) =>
        await ClipboardHelper.CopyAsync(_status, ViewModel.ClinicalNoteText, "Note");
}
