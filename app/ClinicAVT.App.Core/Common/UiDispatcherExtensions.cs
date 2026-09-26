using ClinicAVT.App.Core.Ports;

namespace ClinicAVT.App.Core.Common;

public static class UiDispatcherExtensions
{
    /// <summary>
    /// Posts when there is a dispatcher and runs inline without one, as in a test.
    /// </summary>
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
