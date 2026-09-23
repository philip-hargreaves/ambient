using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel, ShellViewModel shell, VoiceViewModel voice)
    {
        ViewModel = viewModel;
        Shell = shell;
        Voice = voice;
        InitializeComponent();
        Loaded += (_, _) => _ = voice.RefreshAsync();
    }

    public SettingsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    public VoiceViewModel Voice { get; }
}
