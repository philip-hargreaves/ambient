using System.Globalization;
using System.Text.Json;

namespace Ambient.App.Core.Features.Guidance;

/// <summary>One recommendation as the wire gives it.</summary>
public sealed record GuidanceRecommendation(
    string Corpus, string ChunkId, string Code, string Number, string Title, string Section,
    string Text, string Link, string LastUpdated, string UpdateTag, string Source,
    string Citation, string Trigger, string SourceLabel, bool FromNote,
    long Document = 0, int Page = 0, int Pages = 0, bool Labelled = false)
{
    /// <summary>Labelled when the corpus manifest names its publisher for the chip.</summary>
    public static GuidanceRecommendation From(
        JsonElement result, string sourceLabel, bool fromNote, bool labelled = false)
    {
        return new(
            Field(result, "corpus"), Field(result, "chunkId"), Field(result, "code"),
            Field(result, "number"), Field(result, "title"), Field(result, "section"),
            Field(result, "text").TrimEnd(), Field(result, "url"), Field(result, "lastUpdated"),
            Field(result, "updateTag"), Field(result, "source"), Field(result, "citation"),
            Field(result, "trigger"), sourceLabel, fromNote,
            Integer(result, "document"), (int)Numeric(result, "page"),
            (int)Numeric(result, "pages"), labelled);
    }

    /// <summary>A passage from a document the clinician added.</summary>
    public bool FromDocument => Source == "upload";

    /// <summary>"Page 2" of an added PDF, empty for anything without pages.</summary>
    public string PageLabel => Pages > 0 ? $"Page {Page + 1}" : "";

    public bool PageLabelVisible => PageLabel.Length > 0;

    /// <summary>
    /// "NG100 1.1.1", the code alone, or for a document its number, its page or its title.
    /// </summary>
    public string Reference => Code.Length > 0
        ? Number.Length > 0 ? $"{Code.ToUpperInvariant()} {Number}" : Code.ToUpperInvariant()
        : Number.Length > 0 ? Number
        : PageLabel.Length > 0 ? PageLabel
        : Title;

    /// <summary>"[2009, amended 2018]" as the guideline marks it, empty when unmarked.</summary>
    public string Tag => UpdateTag.Length > 0 ? $"[{UpdateTag}]" : "";

    public bool TagVisible => UpdateTag.Length > 0;

    /// <summary>The section path under the title. The wire's " > " reads as "›".</summary>
    public string Path => Section.Replace(" > ", " › ", StringComparison.Ordinal);

    public bool PathVisible => Section.Length > 0;

    public bool LabelVisible =>
        Number.Length > 0 || Section.Length > 0 || UpdateTag.Length > 0 || PageLabelVisible;

    /// <summary>
    /// The note sentence that found it, or the whole note. A typed query has no line.
    /// </summary>
    public string Matched => Trigger.Length > 0 ? $"Matched: “{Trigger}”"
        : FromNote ? "Matched: the note as a whole"
        : "";

    public bool MatchedVisible => Matched.Length > 0;

    /// <summary>A web link opens in the browser, a document as a copy in the PDF viewer.</summary>
    public bool CanOpen => FromDocument || HasWebLink;

    /// <summary>A plain-text corpus carries a file name here, which nothing can open.</summary>
    private bool HasWebLink =>
        Uri.TryCreate(Link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>The citation, with the web address on its own line when there is one.</summary>
    public string CitationText => HasWebLink ? $"{Citation}\n{Link}" : Citation;

    public string OpenTip => FromDocument ? "Opens the file in your PDF viewer." : "";

    /// <summary>Show in document: the page view, for a document with pages.</summary>
    public bool ShowVisible => FromDocument && Pages > 0;

    public string ShowName => $"Show {Reference} in the document";

    public string OpenName => FromDocument ? $"Open {Title}" : $"Open {Reference}";

    public string CopyName => $"Copy citation for {Reference}";

    internal static string Field(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    internal static double Numeric(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    // A document id is a random 63-bit number, past what a double keeps exactly
    internal static long Integer(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var integer)
            ? integer
            : 0;
}

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

    /// <summary>The corpus source on the wire, upload for an added document.</summary>
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
            var count = Count(Recommendations.Count, "recommendation");
            var date = ShortDate(First.LastUpdated);
            if (!FromDocument)
            {
                return date.Length > 0 ? $"Updated {date} · {count}" : count;
            }

            var parts = new List<string>();
            if (First.Pages > 0)
            {
                parts.Add(Count(First.Pages, "page"));
            }

            if (date.Length > 0)
            {
                parts.Add($"added {date}");
            }

            parts.Add(count);
            return string.Join(" · ", parts);
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

    /// <summary>"1 page", "12 pages".</summary>
    internal static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";
}
