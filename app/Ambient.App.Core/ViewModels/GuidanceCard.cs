using System.Globalization;
using System.Text.Json;

namespace Ambient.App.Core.ViewModels;

/// <summary>One recommendation as the wire gives it.</summary>
public sealed record GuidanceRecommendation(
    string Corpus, string ChunkId, string Code, string Number, string Title, string Section,
    string Text, string Link, string LastUpdated, string UpdateTag, string Source,
    string Citation, double Score, string Trigger, string SourceLabel, bool FromNote)
{
    public static GuidanceRecommendation From(JsonElement result, string sourceLabel, bool fromNote)
    {
        var score = result.TryGetProperty("score", out var value)
            && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        return new(
            Field(result, "corpus"), Field(result, "chunkId"), Field(result, "code"),
            Field(result, "number"), Field(result, "title"), Field(result, "section"),
            Field(result, "text").TrimEnd(), Field(result, "url"), Field(result, "lastUpdated"),
            Field(result, "updateTag"), Field(result, "source"), Field(result, "citation"),
            score, Field(result, "trigger"), sourceLabel, fromNote);
    }

    /// <summary>"NG100 1.1.1", or the code alone when the recommendation has no number.</summary>
    public string Reference => Number.Length > 0
        ? $"{Code.ToUpperInvariant()} {Number}"
        : Code.ToUpperInvariant();

    /// <summary>"[2009, amended 2018]" as the guideline marks it, empty when unmarked.</summary>
    public string Tag => UpdateTag.Length > 0 ? $"[{UpdateTag}]" : "";

    public bool TagVisible => UpdateTag.Length > 0;

    /// <summary>The section path under the title. The wire's " > " reads as "›".</summary>
    public string Path => Section.Replace(" > ", " › ", StringComparison.Ordinal);

    public bool PathVisible => Section.Length > 0;

    public bool LabelVisible => Number.Length > 0 || Section.Length > 0 || UpdateTag.Length > 0;

    /// <summary>
    /// The note sentence that found it, or the whole note. A typed query has no line.
    /// </summary>
    public string Matched => Trigger.Length > 0 ? $"Matched: “{Trigger}”"
        : FromNote ? "Matched: the note as a whole"
        : "";

    public bool MatchedVisible => Matched.Length > 0;

    /// <summary>A plain-text corpus carries a file name here, which nothing can open.</summary>
    public bool CanOpen =>
        Uri.TryCreate(Link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public string OpenName => $"Open {Reference}";

    public string CopyName => $"Copy citation for {Reference}";

    internal static string Field(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

/// <summary>One guideline, or one document, with the recommendations found in it.</summary>
public sealed record GuidanceCard(IReadOnlyList<GuidanceRecommendation> Recommendations)
{
    /// <summary>One card per guideline, in the order each first appears.</summary>
    public static IEnumerable<GuidanceCard> Group(IEnumerable<GuidanceRecommendation> shown) =>
        shown.GroupBy(r => (r.Corpus, r.Code)).Select(g => new GuidanceCard([.. g]));

    private GuidanceRecommendation First => Recommendations[0];

    public string Corpus => First.Corpus;

    public string Code => First.Code.ToUpperInvariant();

    public string Title => First.Title;

    public string SourceLabel => First.SourceLabel;

    /// <summary>
    /// "NICE · NG100", a text corpus by name, or the code when no source was named.
    /// </summary>
    public string Chip => First.Source == "nice" && SourceLabel.Length > 0
        ? $"{SourceLabel} · {Code}"
        : SourceLabel.Length > 0 ? SourceLabel : Code;

    /// <summary>
    /// "Updated 12 Oct 2020 · 3 recommendations", or the count alone without a date.
    /// </summary>
    public string Meta
    {
        get
        {
            var count = Recommendations.Count == 1
                ? "1 recommendation"
                : $"{Recommendations.Count} recommendations";
            var date = ShortDate(First.LastUpdated);
            return date.Length > 0 ? $"Updated {date} · {count}" : count;
        }
    }

    /// <summary>"12 Oct 2020" from any ISO 8601 date, empty from anything else.</summary>
    internal static string ShortDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "";

    internal static string Field(JsonElement element, string property) =>
        GuidanceRecommendation.Field(element, property);
}
