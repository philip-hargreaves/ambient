using System.Diagnostics.CodeAnalysis;
using ClinicAVT.App.Core.Ports;
using ClinicAVT.Client;

namespace ClinicAVT.App.Core.Common;

public static class EngineApiExtensions
{
    /// <summary>False without an engine, as in a test, and while the engine is down.</summary>
    public static bool IsConnected([NotNullWhen(true)] this IEngineApi? engine) =>
        engine is not null && engine.Connected;

    /// <summary>
    /// Runs the action on every connect, through the dispatcher when there is one. Runs it at
    /// once when the engine is already connected.
    /// </summary>
    public static void OnConnected(this IEngineApi engine, IUiDispatcher? dispatcher, Action action)
    {
        engine.ConnectedChanged += connected =>
        {
            if (connected)
            {
                dispatcher.PostOrRun(action);
            }
        };
        if (engine.Connected)
        {
            action();
        }
    }
}
