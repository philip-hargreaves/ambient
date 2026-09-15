using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ambient.App.Core.ViewModels;

/// <summary>One added document, as Settings lists it.</summary>
public sealed partial class DocumentRow : ObservableObject
{
    public DocumentRow(JsonElement document, IRelayCommand<DocumentRow>? remove = null)
    {
        Id = document.GetProperty("id").GetInt64();
        Name = GuidanceCard.Field(document, "name");
        Remove = remove;
        Apply(document);
    }

    public long Id { get; }

    public string Name { get; }

    /// <summary>Cancel while the row works, remove once it has settled.</summary>
    public IRelayCommand<DocumentRow>? Remove { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Working), nameof(Failed), nameof(Plain), nameof(Waiting),
        nameof(ControlLabel), nameof(ControlVisible))]
    public partial string State { get; private set; } = "indexing";

    [ObservableProperty]
    public partial string Detail { get; private set; } = "Waiting";

    /// <summary>Passages embedded so far, as a fraction of the document.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    // What the ingest last said it was doing, empty before it starts
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Waiting), nameof(ControlVisible))]
    public partial string Phase { get; private set; } = "";

    public bool Working => State == "indexing";

    public bool Failed => State is "failed" or "stale";

    public bool Plain => !Failed;

    public bool Waiting => Working && Phase.Length == 0;

    public string ControlLabel => Working ? "Cancel" : "Remove";

    /// <summary>Nothing to cancel while the consultation has the machine.</summary>
    public bool ControlVisible => Phase != "paused";

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

    private static string Describe(JsonElement document) =>
        GuidanceCard.Field(document, "state") switch
        {
            "ready" => Ready(document),
            "failed" => GuidanceCard.Field(document, "error") == "patientData"
                ? "Not added: this looks like a document about a patient."
                : "Could not be read. Remove it and add the file again.",
            "stale" => "Added with an earlier guidance model. Remove it and add the file again.",
            _ => "Waiting",
        };

    private static string Ready(JsonElement document)
    {
        var parts = new List<string>();
        var pages = Int(document, "pages");
        if (pages > 0)
        {
            parts.Add(Count(pages, "page"));
        }

        parts.Add(Count(Int(document, "chunks"), "passage"));
        var added = GuidanceCard.ShortDate(GuidanceCard.Field(document, "addedAt"));
        if (added.Length > 0)
        {
            parts.Add($"added {added}");
        }

        return string.Join(" · ", parts);
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";

    private static int Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var n) ? n : 0;
}
