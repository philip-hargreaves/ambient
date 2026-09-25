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
    private readonly GuidanceSectionView _guidance;
    private bool _guidanceBelow = true;

    public NoteEditorView(
        NoteViewModel viewModel, DocumentExportViewModel export, GuidanceSectionView guidance)
    {
        ViewModel = viewModel;
        Export = export;
        _guidance = guidance;
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

    // Two radio groups, style then detail, applied on the next Regenerate
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

    private static RadioMenuFlyoutItem OptionItem(string group, NoteOption option, Action<string> choose)
    {
        var item = new RadioMenuFlyoutItem { Text = option.Name, GroupName = group, Tag = option.Value };
        item.Click += (_, _) => choose(option.Value);
        return item;
    }

    // The chosen item in each group is checked; the group unchecks the rest. Rechecked as the
    // menu opens, since an item checked before it has ever shown may not draw its mark
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

    public NoteViewModel ViewModel { get; }

    public DocumentExportViewModel Export { get; }

    /// <summary>The Guidelines section, for a host that shows it beside the note.</summary>
    public GuidanceSectionView Guidance => _guidance;

    public void PlaceGuidance(bool below)
    {
        _guidanceBelow = below;
        GuidanceHost.Content = below ? _guidance : null;
    }

    // Guidance below: the note keeps at most half of the area and guidance gets the
    // rest. Elsewhere: the note may take all of the area but the rows around it
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
