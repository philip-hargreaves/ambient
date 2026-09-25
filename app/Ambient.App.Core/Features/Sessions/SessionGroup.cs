using System.Collections.ObjectModel;

namespace Ambient.App.Core.Features.Sessions;

/// <summary>One day's consultations, newest first.</summary>
public sealed class SessionGroup(string day) : ObservableCollection<SessionRow>
{
    public string Day { get; } = day;
}
