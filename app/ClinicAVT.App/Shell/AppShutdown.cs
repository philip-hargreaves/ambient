using Microsoft.Extensions.Logging;
using ClinicAVT.App.Core.Features.Consultation;
using ClinicAVT.App.Core.Hosting;
using ClinicAVT.App.Core.Ports;

namespace ClinicAVT.App.Shell;

/// <summary>
/// Closes the app. It asks first during a recording, then saves the review's edits, closes
/// the connection and stops the engine, in that order.
/// </summary>
internal sealed class AppShutdown(
    ConsultationViewModel session, IDialogService dialogs, EngineConnection connection,
    IEngineHost host, ILogger<AppShutdown> logger)
{
    /// <summary>False when the clinician chose to keep recording.</summary>
    public async Task<bool> ConfirmAsync() =>
        !(session.ConsultationActive && session.State is SessionState.Recording or SessionState.Finalising)
        || await dialogs.ConfirmAsync(
            "Close during a consultation?",
            "The recording so far is kept; the note will not be written.", "Close", "Keep recording");

    public async Task StopAsync()
    {
        try
        {
            await session.CloseReviewAsync();
            await connection.DisposeAsync();
            host.Shutdown();
        }
        catch (Exception e)
        {
            logger.ShutdownStepFailed(e);
        }
    }
}
