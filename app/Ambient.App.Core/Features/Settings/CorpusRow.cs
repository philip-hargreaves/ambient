namespace Ambient.App.Core.Features.Settings;

/// <summary>One installed corpus, as Settings lists it.</summary>
public sealed record CorpusRow(string Name, string Detail, string Attribution, bool Refused)
{
    public bool AttributionVisible => Attribution.Length > 0;

    public bool Loaded => !Refused;
}
