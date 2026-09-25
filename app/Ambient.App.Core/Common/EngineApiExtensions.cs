using System.Diagnostics.CodeAnalysis;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Common;

public static class EngineApiExtensions
{
    /// <summary>False without an engine (a test) and while the one there is down.</summary>
    public static bool IsConnected([NotNullWhen(true)] this IEngineApi? engine) =>
        engine is not null && engine.Connected;

    /// <summary>Runs the action on every connect, posted when there is a dispatcher, and now if already connected.</summary>
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
