namespace ClinicAVT.App.Core.Shell;

/// <summary>The surface on screen and the way back through the ones before it.</summary>
public sealed class NavigationHistory<T>
    where T : class
{
    private readonly Stack<T> _back = new();

    public T? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    /// <summary>Shows a surface, keeping the current one to come back to.</summary>
    public void Show(T surface)
    {
        if (Current is not null)
        {
            _back.Push(Current);
        }

        Current = surface;
    }

    /// <summary>
    /// Makes the previous surface current and returns it. Null when there is none.
    /// </summary>
    public T? Back()
    {
        if (_back.Count == 0)
        {
            return null;
        }

        Current = _back.Pop();
        return Current;
    }
}
