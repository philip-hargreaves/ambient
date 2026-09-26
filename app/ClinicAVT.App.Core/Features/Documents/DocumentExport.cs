namespace ClinicAVT.App.Core.Features.Documents;

public static class DocumentExport
{
    /// <summary>
    /// The first line of an exported note or sheet. It tells a clinician not to file
    /// it as a guideline, and the engine's patient-data screen refuses any file that
    /// carries it, so an export dropped in the guidelines folder is never searched.
    /// </summary>
    public const string Marker = "ClinicAVT export - not for the guidelines folder.\n\n";
}
