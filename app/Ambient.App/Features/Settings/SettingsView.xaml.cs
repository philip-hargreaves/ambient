using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Settings;

namespace Ambient.App.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel, VoiceViewModel voice)
    {
        ViewModel = viewModel;
        Voice = voice;
        InitializeComponent();
        Loaded += (_, _) => _ = voice.RefreshAsync();
    }

    public SettingsViewModel ViewModel { get; }

    public VoiceViewModel Voice { get; }
}
