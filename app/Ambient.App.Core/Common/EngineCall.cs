using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Common;

/// <summary>An engine call whose failure is one line, not an exception.</summary>
public static class EngineCall
{
    /// <summary>Failure goes on the status line as "{problem}: {reason}". Cancellation still throws.</summary>
    public static async Task<bool> ReportAsync(StatusBarViewModel? status, string problem, Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(true);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status?.Append($"{problem}: {e.Message}");
            return false;
        }
    }

    /// <summary>The call's value, or null when it failed.</summary>
    public static async Task<T?> ReportAsync<T>(StatusBarViewModel? status, string problem, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status?.Append($"{problem}: {e.Message}");
            return default;
        }
    }

    /// <summary>Failure, a cancellation included, goes to the log as "{step} failed: {reason}".</summary>
    public static async Task<bool> LogAsync(StatusBarViewModel? status, string step, Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(true);
            return true;
        }
        catch (Exception e)
        {
            status?.Log($"{step} failed: {e.Message}");
            return false;
        }
    }

    /// <summary>The call's value, or null when it failed.</summary>
    public static async Task<T?> LogAsync<T>(StatusBarViewModel? status, string step, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            status?.Log($"{step} failed: {e.Message}");
            return default;
        }
    }

    /// <summary>
    /// A step inside a flow that carries on when it fails: the failure is reported on the
    /// status line and logged, and nothing is thrown.
    /// </summary>
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
