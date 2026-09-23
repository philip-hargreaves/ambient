using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Features.Consultation;

/// <summary>An engine call inside a flow that carries on when it fails: the failure is reported and logged, and nothing is thrown.</summary>
internal static class EngineStep
{
    public static async Task<bool> TryAsync(StatusBarViewModel status, string step, Func<Task> call) =>
        await TryAsync(status, step, async () =>
        {
            await call().ConfigureAwait(true);
            return true;
        }).ConfigureAwait(true);

    /// <summary>The call's value, or null when it failed.</summary>
    public static async Task<T?> TryAsync<T>(StatusBarViewModel status, string step, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            status.Append("Taking longer than expected", busy: true);
            return default;
        }
        catch (Exception e)
        {
            status.Append("A step failed - trying to continue");
            status.Log($"{step} failed: {e.Message}");
            return default;
        }
    }
}
