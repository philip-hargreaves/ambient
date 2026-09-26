namespace ClinicAVT.App.Core.Ports;

/// <summary>The pickers for everything that reads or writes outside the encrypted store.</summary>
public interface IFilePicker
{
    /// <summary>The chosen path, or null when cancelled.</summary>
    Task<string?> PickSaveAsync(string suggestedName, string typeLabel, string extension);

    /// <summary>One file of the given extension, or null when cancelled.</summary>
    Task<string?> PickFileAsync(string extension);

    /// <summary>Any number of files of the given extensions, empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync(IReadOnlyList<string> extensions);
}
