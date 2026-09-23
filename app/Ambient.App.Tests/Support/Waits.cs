namespace Ambient.App.Tests.Support;

/// <summary>Waiting for fire-and-forget work to land, by its effect rather than by a fixed sleep.</summary>
internal static class Waits
{
    private static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(5);

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? limit = null)
    {
        var deadline = DateTime.UtcNow + (limit ?? DefaultLimit);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not reached in time");
            await Task.Delay(10);
        }
    }
}
