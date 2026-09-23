using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Sessions;

namespace Ambient.App.Features.Appraisal;

/// <summary>The Appraisal page: a journal of reflections, one year at a time.</summary>
public sealed partial class AppraisalsView : UserControl
{
    private readonly StatusBarViewModel _status;

    public AppraisalsView(AppraisalsViewModel viewModel, ShellViewModel shell, StatusBarViewModel status,
        IClipboard clipboard, IFilePicker picker, IDialogService dialogs)
    {
        ViewModel = viewModel;
        Shell = shell;
        _status = status;
        ReflectionCardView.EditorFactory = card => new ReflectionEditorView(
            card.Editor!, status, clipboard, picker, dialogs, showHeading: false,
            removeCommand: card.DeleteCommand);
        InitializeComponent();
        MonthMarkerView.Pressed = OnMonthPressed;
        Loaded += (_, _) => _ = ViewModel.RefreshAsync();
    }

    public AppraisalsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    private async void OnBackClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.LeaveAsync();
        Shell.GoBackCommand.Execute(null);
    }

    // A month with entries filters to it; an empty one says so
    private void OnMonthPressed(MonthMarker month)
    {
        if (month.Filled)
        {
            ViewModel.ToggleMonth(month.Month);
            return;
        }

        _status.Append($"No reflections in {ViewModel.MonthName(month.Month)}");
    }
}
