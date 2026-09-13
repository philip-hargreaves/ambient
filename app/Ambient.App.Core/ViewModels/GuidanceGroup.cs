namespace Ambient.App.Core.ViewModels;

/// <summary>
/// Cards under one heading: the note sentence that found them, the note as a
/// whole, or a typed query. The tip carries the full sentence or query for a
/// header the layout may truncate; null when there is nothing more to show.
/// </summary>
public sealed record GuidanceGroup(
    string Header, string? HeaderTip, bool IsQuery, IReadOnlyList<GuidanceCard> Cards)
{
    public static GuidanceGroup ForTrigger(string trigger, IReadOnlyList<GuidanceCard> cards) =>
        trigger.Length > 0
            ? new($"For: '{trigger}'", trigger, false, cards)
            : new("For the note as a whole", null, false, cards);

    public static GuidanceGroup ForQuery(string query, IReadOnlyList<GuidanceCard> cards) =>
        new($"Search: '{query}'", query, true, cards);
}
