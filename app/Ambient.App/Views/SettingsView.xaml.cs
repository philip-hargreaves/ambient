using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Ambient.App.Core;
using Ambient.App.Core.ViewModels;
using Ambient.Client;

namespace Ambient.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel, ShellViewModel shell, VoiceViewModel voice,
        IEngineClient engine, MicViewModel mic, IUiDispatcher dispatcher)
    {
        ViewModel = viewModel;
        Shell = shell;
        Voice = voice;
        InitializeComponent();
        // Forgetting the voiceprint cannot be undone, so it is asked once
        voice.ConfirmForget = async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Forget voice enrolment?",
                Content = "It will be learned again from your next consultation.",
                PrimaryButtonText = "Forget",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        };
        // One reading per dialog; the outcome says whether a print was kept
        voice.RunEnrolment = async () =>
        {
            using var enrolment = new EnrolmentViewModel(engine, mic.MicId, dispatcher: dispatcher);
            var dialog = new EnrolmentDialog(enrolment) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            return await enrolment.Outcome;
        };
        voice.NotifyCommands();
        Loaded += (_, _) => _ = voice.RefreshAsync();
        // Turning the history on accumulates patient records: confirmed,
        // never just toggled. Cancel is the safe default.
        viewModel.PickSavePath = suggested =>
            SavePickerHelper.PickAsync(suggested, "HTML report", ".html");
        viewModel.ConfirmDeleteAllConsultations = async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Delete all consultation data?",
                Content = "Every stored consultation on this device is erased: transcripts, notes, "
                    + "patient sheets and appraisal reflections. This cannot be undone.",
                PrimaryButtonText = "Delete all",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        };
        viewModel.ConfirmKeepConsultations = async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Save consultation data?",
                Content = "Transcripts, notes and patient sheets will be stored encrypted on "
                    + "this device.\n\nContinue only if you have the necessary consent and approval.",
                PrimaryButtonText = "Turn on",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        };
        // Two pickers because Windows has two dialogs. The file one takes many
        viewModel.PickDocuments = async () =>
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            if (!BindToWindow(picker))
            {
                return [];
            }

            var files = await picker.PickMultipleFilesAsync();
            return files.Select(f => f.Path).ToArray();
        };
        viewModel.PickFolder = async () =>
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");
            if (!BindToWindow(picker))
            {
                return null;
            }

            return (await picker.PickSingleFolderAsync())?.Path;
        };
        viewModel.ConfirmRemoveDocument = name => ConfirmAsync($"Remove {name}?",
            "It is erased from this device. Guidance already saved with a consultation "
            + "is unchanged.", "Remove");
        viewModel.ConfirmRemoveAllDocuments = count => ConfirmAsync(
            count == 1 ? "Remove the added document?" : $"Remove all {count} documents?",
            "They are erased from this device. This cannot be undone.", "Remove all");
    }

    public SettingsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    public VoiceViewModel Voice { get; }

    // Unpackaged WinUI: a picker must be bound to our window handle
    private static bool BindToWindow(object picker)
    {
        var window = App.Current.Window;
        if (window is null)
        {
            return false;
        }

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return true;
    }

    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
