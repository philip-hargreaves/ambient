using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Ambient.App.Core.ViewModels;

namespace Ambient.App.Views;

/// <summary>The Guidelines section under the clinical note, hosted by the note editor.</summary>
public sealed partial class GuidanceSectionView : UserControl
{
    private readonly StatusBarViewModel _status;
    private readonly DispatcherQueueTimer _timingTimer;
    private Storyboard? _fade;

    public GuidanceSectionView(
        GuidanceViewModel viewModel, ShellViewModel shell, StatusBarViewModel status)
    {
        ViewModel = viewModel;
        Shell = shell;
        _status = status;
        InitializeComponent();
        _timingTimer = DispatcherQueue.CreateTimer();
        _timingTimer.Interval = TimeSpan.FromSeconds(4);
        _timingTimer.IsRepeating = false;
        _timingTimer.Tick += (_, _) => FadeTiming();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidanceViewModel.FoundIn) && ViewModel.FoundIn.Length > 0)
            {
                _fade?.Stop();
                Timing.Opacity = 1;
                _timingTimer.Start();
            }
        };
    }

    public GuidanceViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        if (ViewModel.SearchQueryCommand.CanExecute(null))
        {
            ViewModel.SearchQueryCommand.Execute(null);
        }
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        if (ViewModel.ClearQueryCommand.CanExecute(null))
        {
            ViewModel.ClearQueryCommand.Execute(null);
        }

        ViewModel.Query = "";
        e.Handled = true;
    }

    private void FadeTiming()
    {
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400) };
        Storyboard.SetTarget(fade, Timing);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _fade = new Storyboard();
        _fade.Children.Add(fade);
        _fade.Begin();
    }

    private void OnRowEntered(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuidanceRecommendation found)
        {
            ViewModel.Hovered = found.Trigger;
        }
    }

    private void OnRowExited(object sender, PointerRoutedEventArgs e) => ViewModel.Hovered = "";

    // Open shows an up chevron, folded a down one, as the patient sheet's fold does
    private void OnFoldClick(object sender, RoutedEventArgs e)
    {
        var open = Body.Visibility == Visibility.Visible;
        Body.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        SearchRow.Visibility = Body.Visibility;
        FoldGlyph.Glyph = open ? "" : "";
    }

    /// <summary>A tooltip only when there is something to say.</summary>
    public static object? Tip(string tip) => tip.Length == 0 ? null : tip;

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GuidanceRecommendation found)
        {
            return;
        }

        if (found.FromDocument)
        {
            await ViewModel.OpenDocumentAsync(found);
        }
        else
        {
            await LinkHelper.OpenAsync(_status, found.Link);
        }
    }

    private async void OnShowInDocument(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuidanceRecommendation found)
        {
            PageView.Opener = sender as FrameworkElement;
            await ViewModel.ShowInDocumentAsync(found);
        }
    }

    private async void OnCopyCitation(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GuidanceRecommendation found)
        {
            await ClipboardHelper.CopyAsync(_status, found.CitationText, "Citation");
        }
    }
}
