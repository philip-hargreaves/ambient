namespace Ambient.App.Core.Ports;

/// <summary>Hands files, folders and links to the programs that open them.</summary>
public interface ILauncher
{
    /// <summary>A web link in the default browser; false for anything that is not http(s) or when no browser answered.</summary>
    Task<bool> OpenLinkAsync(string link);

    /// <summary>A file in its viewer; throws when no viewer answered.</summary>
    Task OpenFileAsync(string path);

    /// <summary>A folder in the file manager; best effort.</summary>
    void RevealFolder(string path);
}
