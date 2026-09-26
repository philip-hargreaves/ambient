using ClinicAVT.App.Core.Common;

namespace ClinicAVT.App.Core.Features.Guidance;

/// <summary>One guideline, or one document, with the recommendations found in it.</summary>
public sealed record GuidanceCard(IReadOnlyList<GuidanceRecommendation> Recommendations)
{
    /// <summary>One card per guideline, in the order each first appears.</summary>
    public static IEnumerable<GuidanceCard> Group(IEnumerable<GuidanceRecommendation> shown) =>
        shown.GroupBy(r => (r.Corpus, r.Code)).Select(g => new GuidanceCard([.. g]));

    /// <summary>"From NICE" above the first guideline after the added documents.</summary>
    public string Divider { get; init; } = "";

    public bool DividerVisible => Divider.Length > 0;

    private GuidanceRecommendation First => Recommendations[0];

    public bool FromDocument => First.FromDocument;

    public string Code => First.Code.ToUpperInvariant();

    public string Title => First.Title;

    public string SourceLabel => First.SourceLabel;

    public bool Labelled => First.Labelled;

    /// <summary>The corpus source on the wire, "upload" for an added document.</summary>
    public string Source => First.Source;

    /// <summary>
    /// "Added document", the manifest's label with the code, "NICE · NG100", a corpus by
    /// name, or the code alone.
    /// </summary>
    public string Chip => FromDocument ? "Added document"
        : First.Labelled && Code.Length > 0 ? $"{SourceLabel} · {Code}"
        : SourceLabel.Length > 0 ? SourceLabel
        : Code;

    /// <summary>
    /// "Updated 12 Oct 2020 · 3 recommendations", or for a document
    /// "5 pages · added 15 Sep 2026 · 3 recommendations".
    /// </summary>
    public string Meta
    {
        get
        {
            var count = Words.Count(Recommendations.Count, "recommendation");
            var date = Words.ShortDate(First.LastUpdated);
            if (!FromDocument)
            {
                return date.Length > 0 ? $"Updated {date} · {count}" : count;
            }

            var parts = new List<string>();
            if (First.Pages > 0)
            {
                parts.Add(Words.Count(First.Pages, "page"));
            }

            if (date.Length > 0)
            {
                parts.Add($"added {date}");
            }

            parts.Add(count);
            return string.Join(" · ", parts);
        }
    }
}
