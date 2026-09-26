using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ClinicAVT.App.Core.Features.Appraisal;

namespace ClinicAVT.App.Features.Appraisal;

/// <summary>
/// The case study, the guidance ticks and the three questions, used by the sheet and the journal cards.
/// </summary>
public sealed partial class ReflectionEditorView : UserControl
{
    public ReflectionEditorView(
        ReflectionViewModel viewModel, bool showHeading = true, ICommand? removeCommand = null)
    {
        ViewModel = viewModel;
        ShowHeading = showHeading;
        RemoveCommand = removeCommand;
        InitializeComponent();
    }

    public ReflectionViewModel ViewModel { get; }

    /// <summary>The title and month. Off inside a journal card, whose header carries them.</summary>
    public bool ShowHeading { get; }

    /// <summary>Removes the entry. Only a journal card offers it.</summary>
    public ICommand? RemoveCommand { get; }

    public bool HasRemove => RemoveCommand is not null;

    // The boxes bind on every keystroke, so the view model is current by LostFocus
    private async void OnAnswerCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();

    private async void OnTitleCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveTitleAsync();

    private async void OnSummaryCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveSummaryAsync();

    // Pressing Enter or leaving the box turns the typed line into an entry
    private async void OnDraftKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.AddDraftCommand.ExecuteAsync(null);
        }
    }

    private async void OnDraftLeft(object sender, RoutedEventArgs e) => await ViewModel.AddDraftCommand.ExecuteAsync(null);
}
