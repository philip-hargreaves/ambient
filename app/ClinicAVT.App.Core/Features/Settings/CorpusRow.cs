namespace ClinicAVT.App.Core.Features.Settings;

public sealed record CorpusRow(string Name, string Detail, string Attribution, bool Refused)
{
    public bool AttributionVisible => Attribution.Length > 0;

    public bool Loaded => !Refused;
}
