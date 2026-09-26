namespace ClinicAVT.App.Core.Features.Consultation;

/// <summary>
/// One line of the microphone picker. Label marks the default device. Note is the caption
/// for a Bluetooth call-mode device and is empty for any other.
/// </summary>
public sealed record MicRow(string Label, string Note, bool IsChecked, string Id)
{
    public bool NoteVisible => Note.Length > 0;
}
