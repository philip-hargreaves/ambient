namespace Ambient.App.Core.Ports;

/// <summary>
/// The dialogs the shell shows on a view model's behalf. Confirmations default
/// to cancel. The two flows own their view model for the dialog's lifetime.
/// </summary>
public interface IDialogService
{
    /// <summary>True when the primary button was chosen. The other button cancels.</summary>
    Task<bool> ConfirmAsync(string title, string content, string primary, string cancel = "Cancel");

    /// <summary>The voice enrolment dialog. True when a print was made.</summary>
    Task<bool> RunEnrolmentAsync();

    /// <summary>The reflection sheet for a stored consultation. Closing saves.</summary>
    Task ShowReflectionAsync(string sessionId, string startedAt);
}
