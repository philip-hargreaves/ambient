using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Appraisal;
using Ambient.App.Features.Settings;
using Ambient.Client;

namespace Ambient.App.Platform;

public sealed class WinUiDialogService(
    WindowAccessor window, IEngineApi engine, IUiDispatcher dispatcher, MicViewModel mic,
    StatusBarViewModel status, IClipboard clipboard, IFilePicker picker) : IDialogService
{
    // Cancel is the safe default in every confirmation
    public async Task<bool> ConfirmAsync(string title, string content, string primary, string cancel)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = window.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // One reading per dialog. The outcome says whether a print was kept
    public async Task<bool> RunEnrolmentAsync()
    {
        using var enrolment = new EnrolmentViewModel(engine, mic.MicId, dispatcher: dispatcher);
        var dialog = new EnrolmentDialog(enrolment) { XamlRoot = window.XamlRoot };
        await dialog.ShowAsync();
        return await enrolment.Outcome;
    }

    public async Task ShowReflectionAsync(string sessionId, string startedAt)
    {
        // The dialog disposes it as it closes. This covers a load that never showed one
        using var reflection = new ReflectionViewModel(engine, dispatcher, clipboard, picker, this, status);
        await reflection.LoadAsync(sessionId, startedAt);
        var dialog = new ReflectionDialog(reflection) { XamlRoot = window.XamlRoot };
        await dialog.ShowAsync();
    }
}
