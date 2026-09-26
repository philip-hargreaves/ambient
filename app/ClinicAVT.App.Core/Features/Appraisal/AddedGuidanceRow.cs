using System.Windows.Input;
using ClinicAVT.Client;

namespace ClinicAVT.App.Core.Features.Appraisal;

/// <summary>One line of guidance the clinician typed themselves, with its Remove.</summary>
public sealed class AddedGuidanceRow
{
    internal AddedGuidanceRow(ReflectionReference stored, ICommand removeCommand)
    {
        Stored = stored;
        RemoveCommand = removeCommand;
    }

    public string Title => Stored.Title;

    public ICommand RemoveCommand { get; }

    internal ReflectionReference Stored { get; }
}
