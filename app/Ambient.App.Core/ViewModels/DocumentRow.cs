using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ambient.App.Core.ViewModels;

/// <summary>One document in the guidelines folder, as Settings lists it.</summary>
public sealed partial class DocumentRow : ObservableObject
{
    public DocumentRow(JsonElement document, IRelayCommand<DocumentRow>? remove = null)
    {
        Id = document.GetProperty("id").GetInt64();
        Name = GuidanceCard.Field(document, "name");
        Where = Subfolder(GuidanceCard.Field(document, "path"));
        Remove = remove;
        Apply(document);
    }

    public long Id { get; }

    public string Name { get; }

    /// <summary>The subfolder crumb when the file sits below the folder root.</summary>
    public string Where { get; }

    public bool Located => Where.Length > 0;

    /// <summary>Remove sends the file to the Recycle Bin.</summary>
    public IRelayCommand<DocumentRow>? Remove { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Working), nameof(Failed), nameof(Plain), nameof(Waiting))]
    public partial string State { get; private set; } = "indexing";

    [ObservableProperty]
    public partial string Detail { get; private set; } = "Waiting";

    /// <summary>Passages embedded so far, as a fraction of the document.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    // What the ingest last said it was doing, empty before it starts
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Waiting))]
    public partial string Phase { get; private set; } = "";

    public bool Working => State == "indexing";

    public bool Failed => State == "failed";

    public bool Plain => !Failed;

    public bool Waiting => Working && Phase.Length == 0;

    /// <summary>guidance/document: the row as the engine now has it.</summary>
    public void Apply(JsonElement document)
    {
        State = GuidanceCard.Field(document, "state");
        Detail = Describe(document);
        if (!Working)
        {
            Phase = "";
            Progress = 0;
        }
    }

    /// <summary>guidance/progress: what the ingest is doing to this document.</summary>
    public void ApplyProgress(JsonElement progress)
    {
        var done = Int(progress, "done");
        var total = Int(progress, "total");
        Phase = GuidanceCard.Field(progress, "phase");
        Progress = total > 0 ? (double)done / total : 0;
        Detail = Phase switch
        {
            "paused" => "Waiting for the consultation to finish",
            "reading" => total > 1 ? $"Reading page {done} of {total}" : "Reading",
            "preparing" => $"Preparing {done} of {total} passages",
            _ => Detail,
        };
    }

    private static string Subfolder(string path)
    {
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut].Replace('\\', '/').Replace("/", " › ") : "";
    }

    private string Describe(JsonElement document) =>
        State switch
        {
            "ready" => Ready(document),
            "failed" => GuidanceCard.Field(document, "error") switch
            {
                "patientData" => "Not searched: this looks like a document about a patient. "
                    + "Delete it or move it out of the folder.",
                "password" => "Cannot be read: the PDF is password protected.",
                "noText" => $"Cannot be searched: {Int(document, "pagesWithoutText")} of "
                    + $"{Int(document, "pages")} pages are images with no text.",
                _ => "Could not be read.",
            },
            _ => "Waiting",
        };

    private static string Ready(JsonElement document)
    {
        var parts = new List<string>();
        var pages = Int(document, "pages");
        if (pages > 0)
        {
            parts.Add(GuidanceCard.Count(pages, "page"));
        }

        parts.Add(GuidanceCard.Count(Int(document, "chunks"), "passage"));
        var added = GuidanceCard.ShortDate(GuidanceCard.Field(document, "addedAt"));
        if (added.Length > 0)
        {
            parts.Add($"added {added}");
        }

        return string.Join(" · ", parts);
    }

    private static int Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var n) ? n : 0;
}
