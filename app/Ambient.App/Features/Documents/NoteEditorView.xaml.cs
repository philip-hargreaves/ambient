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

    // Inside a tab the note keeps at most half of the content area and the
    // guidance row gets the rest
    public void FitTabContent(FrameworkElement area) => _fit.Fit(area);

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
