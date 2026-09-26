namespace ClinicAVT.App.Core.Features.Guidance;

/// <summary>Where the note's own search has got to.</summary>
public enum GuidanceSection
{
    Hidden,
    FollowsNote,
    Searching,
    Results,
    NothingMatched,
    NoCorpusAtSearch,
    Failed,
    NotSearched,
}
