using Microsoft.UI.Xaml.Controls;
using ClinicAVT.App.Core.Features.Appraisal;
using ClinicAVT.App.Core.Features.Consultation;
using ClinicAVT.App.Core.Features.Settings;
using ClinicAVT.App.Core.Ports;
using ClinicAVT.App.Core.Shell;
using ClinicAVT.App.Features.Appraisal;
using ClinicAVT.App.Features.Settings;
using ClinicAVT.Client;

namespace ClinicAVT.App.Platform;

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

    // A fresh reading per dialog. The outcome says whether it produced a voiceprint
    public async Task<bool> RunEnrolmentAsync()
    {
        using var enrolment = new EnrolmentViewModel(engine, mic.MicId, dispatcher: dispatcher);
        var dialog = new EnrolmentDialog(enrolment) { XamlRoot = window.XamlRoot };
        await dialog.ShowAsync();
        return await enrolment.Outcome;
    }

    public async Task ShowReflectionAsync(string sessionId, string startedAt)
    {
        // The dialog disposes the view model as it closes. The using covers a load that fails first
        using var reflection = new ReflectionViewModel(engine, dispatcher, clipboard, picker, this, status);
        await reflection.LoadAsync(sessionId, startedAt);
        var dialog = new ReflectionDialog(reflection) { XamlRoot = window.XamlRoot };
        await dialog.ShowAsync();
    }
}
