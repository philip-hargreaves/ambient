namespace Ambient.App.Core.Features.Consultation;

/// <summary>
/// One line of the microphone picker: the device, marked when it is the default, its
/// caption when it is a Bluetooth call-mode device, and whether it is the current choice.
/// </summary>
public sealed record MicRow(string Label, string Note, bool IsChecked, string Id)
{
    public bool NoteVisible => Note.Length > 0;
}
