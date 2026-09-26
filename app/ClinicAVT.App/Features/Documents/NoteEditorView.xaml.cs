using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClinicAVT.App.Controls;
using ClinicAVT.App.Core.Features.Documents;
using ClinicAVT.App.Core.Features.Guidance;
using ClinicAVT.App.Core.Preferences;
using ClinicAVT.App.Features.Guidance;

namespace ClinicAVT.App.Features.Documents;

public sealed partial class NoteEditorView : UserControl
{
    private readonly TabFit _fit;
    private bool _guidanceBelow = true;

    public NoteEditorView(
        NoteViewModel viewModel, DocumentExportViewModel export, GuidanceSectionView guidance)
    {
        ViewModel = viewModel;
        Export = export;
        Guidance = guidance;
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
            else if (e.PropertyName is nameof(NoteViewModel.Style) or nameof(NoteViewModel.Detail))
            {
                CheckOptions();
            }
        };
        BuildOptionsMenu();
    }

    public NoteViewModel ViewModel { get; }

    public DocumentExportViewModel Export { get; }

    /// <summary>The Guidelines section, for a host that shows it beside the note.</summary>
    public GuidanceSectionView Guidance { get; }

    public void PlaceGuidance(bool below)
    {
        _guidanceBelow = below;
        GuidanceHost.Content = below ? Guidance : null;
    }

    // With guidance below, the note takes at most half the area. Otherwise it takes the
    // area less the rows around it
    public void FitTabContent(FrameworkElement area)
    {
        if (_guidanceBelow)
        {
            _fit.Fit(area);
            return;
        }

        var chrome = StateRow.ActualHeight + ActionRow.ActualHeight
            + 2 * Editor.RowSpacing + Editor.Padding.Top + Editor.Padding.Bottom;
        _fit.FitWithin(area, chrome);
    }

    private void BuildOptionsMenu()
    {
        foreach (var option in ViewModel.StyleOptions)
        {
            OptionsMenu.Items.Add(OptionItem("style", option, value => ViewModel.Style = value));
        }
        OptionsMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var option in ViewModel.DetailOptions)
        {
            OptionsMenu.Items.Add(OptionItem("detail", option, value => ViewModel.Detail = value));
        }
        OptionsMenu.Opening += (_, _) => CheckOptions();
        CheckOptions();
    }

    private static RadioMenuFlyoutItem OptionItem(string group, NoteOption option, Action<string> choose) =>
        MenuItems.Radio(option.Name, group, isChecked: false, () => choose(option.Value), tag: option.Value);

    // Checking an item unchecks the rest of its group. Runs again on open because an item
    // checked before it first shows may not draw its mark
    private void CheckOptions()
    {
        foreach (var entry in OptionsMenu.Items)
        {
            if (entry is RadioMenuFlyoutItem item
                && item.Tag as string == (item.GroupName == "style" ? ViewModel.Style : ViewModel.Detail))
            {
                item.IsChecked = true;
            }
        }
    }

    // Selects the hovered card's sentence in the read-only note. An open editor keeps its
    // own selection
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
