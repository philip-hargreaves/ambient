using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Documents;

namespace Ambient.App.Features.Sessions;

public sealed partial class SessionsView : UserControl
{
    public SessionsView(SessionsViewModel viewModel, ShellViewModel shell, ReviewSurfaceView surface)
    {
        ViewModel = viewModel;
        Shell = shell;
        InitializeComponent();
        SurfaceHost.Content = surface;
        // A consultation opens on its note, with the guidance beside it when there is any
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionsViewModel.DetailOpen) && ViewModel.DetailOpen)
            {
                surface.Open(preferGuidelines: true);
            }
        };
        Loaded += (_, _) => _ = ViewModel.EnterAsync();
    }

    public SessionsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    // The box binds on every keystroke, so the view model is current by LostFocus
    private async void OnTitleCommitted(object sender, RoutedEventArgs e) => await ViewModel.RenameAsync();
}
