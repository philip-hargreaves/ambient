using Microsoft.Extensions.Logging;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;

namespace Ambient.App.Shell;

/// <summary>
/// The window's close. A recording is asked about first. Then, in order: the review's
/// edits saved, the connection closed, the engine stopped.
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
