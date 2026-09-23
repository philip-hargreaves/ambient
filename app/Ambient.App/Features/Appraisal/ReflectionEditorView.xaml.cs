using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Appraisal;

/// <summary>
/// The case study and the three questions; used by the sheet and the journal cards.
/// </summary>
public sealed partial class ReflectionEditorView : UserControl
{
    private readonly StatusBarViewModel _status;
    private readonly IClipboard _clipboard;
    private readonly IFilePicker _picker;
    private readonly IDialogService _dialogs;

    public ReflectionEditorView(ReflectionViewModel viewModel, StatusBarViewModel status,
        IClipboard clipboard, IFilePicker picker, IDialogService dialogs,
        bool showHeading = true, ICommand? removeCommand = null)
    {
        ViewModel = viewModel;
        _status = status;
        _clipboard = clipboard;
        _picker = picker;
        _dialogs = dialogs;
        ShowHeading = showHeading;
        RemoveCommand = removeCommand;
        InitializeComponent();
    }

    public ReflectionViewModel ViewModel { get; }

    /// <summary>The title and month; off inside a journal card, whose header carries them.</summary>
    public bool ShowHeading { get; }

    /// <summary>Removes the entry; only a journal card offers it.</summary>
    public ICommand? RemoveCommand { get; }

    public bool HasRemove => RemoveCommand is not null;

    // Push the text first: the two-way binding's order against this handler
    // is not guaranteed
    private async void OnAnswerCommitted(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        switch (AutomationProperties.GetName(box))
        {
            case "What stood out":
                ViewModel.Happened = box.Text;
                break;
            case "What did I learn":
                ViewModel.Learned = box.Text;
                break;
            case "Would I do anything differently":
                ViewModel.Next = box.Text;
                break;
            default:
                break;
        }

        await ViewModel.SaveAsync();
    }

    private async void OnTitleCommitted(object sender, RoutedEventArgs e)
    {
        ViewModel.Title = ((TextBox)sender).Text;
        await ViewModel.SaveTitleAsync();
    }

    private async void OnSummaryCommitted(object sender, RoutedEventArgs e)
    {
        var text = ((TextBox)sender).Text;
        if (text == ViewModel.Summary)
        {
            return;
        }

        ViewModel.Summary = text;
        await ViewModel.SaveSummaryAsync();
    }

    private async void OnCopy(object sender, RoutedEventArgs e) =>
        await _clipboard.CopyAsync(_status, ViewModel.ExportText, "Reflection");

    private async void OnSave(object sender, RoutedEventArgs e) =>
        await ReflectionSave.SaveAsync(
            _dialogs, _picker, _status, ViewModel.ExportText, ViewModel.DisplayTitle);
}
