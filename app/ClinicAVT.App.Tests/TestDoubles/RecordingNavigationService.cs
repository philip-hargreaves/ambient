using ClinicAVT.App.Core.Ports;

namespace ClinicAVT.App.Tests.TestDoubles;

/// <summary>Records navigation calls without a real content host.</summary>
internal sealed class RecordingNavigationService : INavigationService
{
    private readonly Stack<string> _back = new();

    public string? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public void NavigateTo(string pageKey)
    {
        if (Current is not null)
        {
            _back.Push(Current);
        }

        Current = pageKey;
    }

    public void GoBack() => Current = _back.Count > 0 ? _back.Pop() : Current;
}
