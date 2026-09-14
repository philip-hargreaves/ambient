using System.Globalization;
using System.Text.Json;

namespace Ambient.App.Core.ViewModels;

/// <summary>One recommendation as the section shows it, read once from the wire.</summary>
public sealed record GuidanceCard(
    string Corpus, string ChunkId, string Code, string Number, string Title, string Section,
    string Text, string Link, string LastUpdated, string UpdateTag, string Source,
    string Citation, double Score, string Trigger, string SourceLabel)
{
    public static GuidanceCard From(JsonElement result, string sourceLabel) => new(
        Field(result, "corpus"), Field(result, "chunkId"), Field(result, "code"),
        Field(result, "number"), Field(result, "title"), Field(result, "section"),
        Field(result, "text").TrimEnd(), Field(result, "url"), Field(result, "lastUpdated"),
        Field(result, "updateTag"), Field(result, "source"), Field(result, "citation"),
        result.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number
            ? score.GetDouble()
            : 0,
        Field(result, "trigger"), sourceLabel);

    /// <summary>"NG100 1.1.1", or the code alone when the recommendation has no number.</summary>
    public string Reference => Number.Length > 0
        ? $"{Code.ToUpperInvariant()} {Number}"
        : Code.ToUpperInvariant();

    /// <summary>"[2009, amended 2018]" as the guideline marks it; empty when unmarked.</summary>
    public string Tag => UpdateTag.Length > 0 ? $"[{UpdateTag}]" : "";

    public bool TagVisible => UpdateTag.Length > 0;

    public bool SourceLabelVisible => SourceLabel.Length > 0;

    /// <summary>Title and section path on one line; the wire's " > " reads as "›".</summary>
    public string Context => Section.Length > 0
        ? $"{Title} › {Section.Replace(" > ", " › ", StringComparison.Ordinal)}"
        : Title;

    /// <summary>The note sentence that found this card; empty when the whole note did.</summary>
    public string Matched => Trigger.Length > 0 ? $"Matched: {Trigger}" : "";

    public bool MatchedVisible => Trigger.Length > 0;

    /// <summary>
    /// "Last updated 12 Oct 2020" from any ISO 8601 date; empty when the guideline gives none.
    /// </summary>
    public string LastUpdatedLabel =>
        ShortDate(LastUpdated) is { Length: > 0 } date ? "Last updated " + date : "";

    public bool LastUpdatedVisible => LastUpdatedLabel.Length > 0;

    /// <summary>A plain-text corpus carries a file name here, which nothing can open.</summary>
    public bool CanOpen =>
        Uri.TryCreate(Link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public string OpenName => $"Open {Reference}";

    public string CopyName => $"Copy citation for {Reference}";

    /// <summary>"12 Oct 2020" from any ISO 8601 date, empty from anything else.</summary>
    internal static string ShortDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "";

    internal static string Field(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
