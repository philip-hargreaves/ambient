using Ambient.App.Core.Common;

namespace Ambient.App.Core.Features.Appraisal;

/// <summary>One month of the year strip: its number, short name, how many entries it holds, and whether it is this month.</summary>
public sealed record MonthMarker(int Month, string Name, int Count, bool Current = false, bool Selected = false)
{
    public bool Filled => Count > 0;

    /// <summary>What pressing the cell does, as its tooltip.</summary>
    public string Tip => Count == 0 ? "No reflections"
        : Selected ? "Show the whole year"
        : $"{Words.Count(Count, "reflection")}, press to show only this month";
}
