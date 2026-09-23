using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Features.Guidance;

namespace Ambient.App.Features.Documents;

public sealed partial class NoteEditorView : UserControl
{
    private readonly TabFit _fit;

    public NoteEditorView(
        NoteViewModel viewModel, DocumentExportViewModel export, GuidanceSectionView guidance)
    {
        ViewModel = viewModel;
        Export = export;
        InitializeComponent();
        GuidanceHost.Content = guidance;
        _fit = new TabFit(NoteBox, 0.5);
        guidance.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidanceViewModel.Hovered))
            {
                LightSentence(guidance.ViewModel.Hovered);
            }
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.NoteEditing))
            {
                EditingChrome.Show(NoteBox, ViewModel.NoteEditing);
            }
        };
    }

    public NoteViewModel ViewModel { get; }

    public DocumentExportViewModel Export { get; }

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

}
