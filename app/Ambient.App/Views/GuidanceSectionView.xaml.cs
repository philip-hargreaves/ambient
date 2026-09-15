using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Ambient.App.Core.ViewModels;

namespace Ambient.App.Views;

/// <summary>The Guidelines section under the clinical note; hosted by the note editor.</summary>
public sealed partial class GuidanceSectionView : UserControl
{
    private readonly StatusBarViewModel _status;

    public GuidanceSectionView(
        GuidanceViewModel viewModel, ShellViewModel shell, StatusBarViewModel status)
    {
        ViewModel = viewModel;
        Shell = shell;
        _status = status;
        InitializeComponent();
    }

    public GuidanceViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter
            && ViewModel.SearchQueryCommand.CanExecute(null))
        {
            ViewModel.SearchQueryCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Open shows an up chevron, folded a down one, as the patient sheet's fold does
    private void OnFoldClick(object sender, RoutedEventArgs e)
    {
        var open = Body.Visibility == Visibility.Visible;
        Body.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        FoldGlyph.Glyph = open ? "" : "";
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuidanceRecommendation found)
        {
            await LinkHelper.OpenAsync(_status, found.Link);
        }
    }

    private async void OnCopyCitation(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuidanceRecommendation found)
        {
            await ClipboardHelper.CopyAsync(_status, found.Citation, "Citation");
        }
    }
}
