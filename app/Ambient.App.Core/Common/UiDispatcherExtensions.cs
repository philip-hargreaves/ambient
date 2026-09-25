using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Common;

public static class UiDispatcherExtensions
{
    /// <summary>Posts when there is a dispatcher, else runs inline (a test without one).</summary>
    public static void PostOrRun(this IUiDispatcher? dispatcher, Action action)
    {
        if (dispatcher is null)
        {
            action();
        }
        else
        {
            dispatcher.Post(action);
        }
    }
}
