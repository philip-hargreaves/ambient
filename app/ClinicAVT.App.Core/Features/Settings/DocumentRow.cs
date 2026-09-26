using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClinicAVT.App.Core.Common;
using ClinicAVT.Client;

namespace ClinicAVT.App.Core.Features.Settings;

/// <summary>One document in the guidelines folder, as Settings lists it.</summary>
public sealed partial class DocumentRow : ObservableObject
{
    public DocumentRow(DocumentInfo document, IRelayCommand<DocumentRow> remove)
    {
        Id = document.Id;
        Name = document.Name ?? "";
        Where = Subfolder(document.Path ?? "");
        Remove = remove;
        Apply(document);
    }

    public long Id { get; }

    public string Name { get; }

    /// <summary>The subfolder crumb when the file sits below the folder root.</summary>
    public string Where { get; }

    public bool Located => Where.Length > 0;

    /// <summary>Remove sends the file to the Recycle Bin.</summary>
    public IRelayCommand<DocumentRow> Remove { get; }

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

    /// <summary>Takes the row's state from a guidance/document notification.</summary>
    public void Apply(DocumentInfo document)
    {
        State = document.State;
        Detail = Describe(document);
        if (!Working)
        {
            Phase = "";
            Progress = 0;
        }
    }

    /// <summary>Takes the ingest's progress from a guidance/progress notification.</summary>
    public void ApplyProgress(GuidanceProgress progress)
    {
        Phase = progress.Phase;
        Progress = progress.Total > 0 ? (double)progress.Done / progress.Total : 0;
        Detail = Phase switch
        {
            "paused" => "Waiting for the consultation to finish",
            "reading" => progress.Total > 1 ? $"Reading page {progress.Done} of {progress.Total}" : "Reading",
            "preparing" => $"Preparing {progress.Done} of {progress.Total} passages",
            _ => Detail,
        };
    }

    private static string Subfolder(string path)
    {
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut].Replace('\\', '/').Replace("/", " › ") : "";
    }

    private string Describe(DocumentInfo document) =>
        State switch
        {
            "ready" => Ready(document),
            "failed" => document.Error switch
            {
                "patientData" => "Not searched: this looks like a document about a patient. "
                    + "Delete it or move it out of the folder.",
                "password" => "Cannot be read: the PDF is password protected.",
                "noText" => $"Cannot be searched: {document.PagesWithoutText} of "
                    + $"{document.Pages} pages are images with no text.",
                _ => "Could not be read.",
            },
            _ => "Waiting",
        };

    private static string Ready(DocumentInfo document)
    {
        var parts = new List<string>();
        if (document.Pages > 0)
        {
            parts.Add(Words.Count(document.Pages, "page"));
        }

        parts.Add(Words.Count(document.Chunks, "passage"));
        var added = Words.ShortDate(document.AddedAt ?? "");
        if (added.Length > 0)
        {
            parts.Add($"added {added}");
        }

        return string.Join(" · ", parts);
    }
}
