using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Settings;

namespace Ambient.App.Features.Settings;

/// <summary>
/// The passage, the ring and the verdict. The primary button is Start, then
/// Finish, then Done or Try again. Cancel stops a reading and keeps nothing.
/// </summary>
public sealed partial class EnrolmentDialog : ContentDialog
{
    public EnrolmentDialog(EnrolmentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public EnrolmentViewModel ViewModel { get; }

    // Done closes; every other press keeps the dialog open for the next step
    private async void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = ViewModel.KeepsOpen;
        await ViewModel.PrimaryCommand.ExecuteAsync(null);
    }

    // Closing covers the close button, Escape and a click outside
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args) => ViewModel.Dismiss();
}
