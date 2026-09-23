using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;

namespace Ambient.App.Features.Appraisal;

/// <summary>
/// The case study and the three questions, used by the sheet and the journal cards.
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

    // The boxes bind as the text changes, so the view model is current when focus leaves
    private async void OnAnswerCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveAsync();

    private async void OnTitleCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveTitleAsync();

    private async void OnSummaryCommitted(object sender, RoutedEventArgs e) => await ViewModel.SaveSummaryAsync();
}
