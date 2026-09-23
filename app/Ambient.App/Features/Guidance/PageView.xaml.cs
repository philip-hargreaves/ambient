using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Ambient.App.Core.Features.Guidance;

namespace Ambient.App.Features.Guidance;

/// <summary>The page of an added document beside the note, the cited lines marked.</summary>
public sealed partial class PageView : UserControl
{
    // Glyph boxes are tight, so a mark reaches a little past the letters
    private const double MarkPad = 4;

    public PageView(PageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // Focus lands on Close as the pane opens and goes back to the link that opened it
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PageViewModel.Visible))
            {
                return;
            }

            if (ViewModel.Visible)
            {
                DispatcherQueue.TryEnqueue(() => CloseButton.Focus(FocusState.Programmatic));
            }
            else
            {
                Opener?.Focus(FocusState.Programmatic);
                Opener = null;
            }
        };
    }

    public PageViewModel ViewModel { get; }

    /// <summary>The control that opened the pane, for focus to return to.</summary>
    public static FrameworkElement? Opener { get; set; }

    public static ImageSource? Bitmap(string path) =>
        path.Length == 0 ? null : new BitmapImage(new Uri(path));

    public static Thickness Offset(double left, double top) =>
        new(left - MarkPad, top - MarkPad, 0, 0);

    public static double Grown(double size) => size + 2 * MarkPad;

    // The sheet fits the pane's width. Ctrl and the wheel zoom from there
    private void OnScrollerSized(object sender, SizeChangedEventArgs e)
    {
        Sheet.Width = Math.Max(0, Scroller.ViewportWidth);
        Placeholder.Width = Sheet.Width;
    }

    // The page arrives at the pane's width, scrolled to the passage
    private void OnPageOpened(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(ShowPassage);

    private void ShowPassage()
    {
        var focus = ViewModel.Focus;
        var viewport = Scroller.ViewportHeight;
        if (focus is null || viewport <= 0 || Scroller.ViewportWidth <= 0 || ViewModel.Width <= 0)
        {
            Scroller.ChangeView(0, 0, 1);
            return;
        }

        // Sheet pixels per bitmap pixel: the page plus its margin fills the width
        const double margin = 24 + 2;
        var scale = Scroller.ViewportWidth / (ViewModel.Width + 2 * margin);
        var top = (margin + focus.Top) * scale;
        var height = focus.Height * scale;
        // Centred when it fits, otherwise its first lines a little below the top
        var offset = height < viewport ? top - (viewport - height) / 2 : top - viewport * 0.1;
        Scroller.ChangeView(0, Math.Max(0, offset), 1);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                ViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.PageUp
                when ViewModel.PreviousPageCommand.CanExecute(null):
                ViewModel.PreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.PageDown when ViewModel.NextPageCommand.CanExecute(null):
                ViewModel.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
