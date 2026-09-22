using Ambient.App.Core.Ports;

namespace Ambient.App.Tests.TestDoubles;

/// <summary>Runs posted work immediately; tests have no UI thread.</summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
