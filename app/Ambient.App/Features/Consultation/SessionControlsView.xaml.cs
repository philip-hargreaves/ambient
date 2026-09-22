using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Consultation;

namespace Ambient.App.Features.Consultation;

public sealed partial class SessionControlsView : UserControl
{
    public SessionControlsView(SessionControlsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SessionControlsViewModel ViewModel { get; }
}
