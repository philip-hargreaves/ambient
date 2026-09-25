namespace Ambient.App.Core.Hosting;

/// <summary>
/// The one-time rename migration from the sotto folder to the ambient one. Moves per
/// item and never overwrites, so a stray ambient folder cannot block the real data and a
/// failure part way leaves every file where it can be found.
/// </summary>
public static class SottoMigration
{
    public const string OldDatabase = "sotto.db";

    public const string NewDatabase = "ambient.db";

    private static readonly string[] DatabaseSuffixes = ["", "-wal", "-shm"];

    /// <summary>True when there was a sotto folder and it is now empty and gone.</summary>
    public static bool Run(string oldRoot, string newRoot)
    {
        if (!Directory.Exists(oldRoot))
        {
            return false;
        }

        RenameDatabase(Path.Combine(oldRoot, "store"));
        Merge(oldRoot, newRoot);
        if (Directory.EnumerateFileSystemEntries(oldRoot).Any())
        {
            return false;
        }

        Directory.Delete(oldRoot);
        return true;
    }

    // The database and its journal files rename together
    private static void RenameDatabase(string store)
    {
        if (!File.Exists(Path.Combine(store, OldDatabase)))
        {
            return;
        }

        foreach (var suffix in DatabaseSuffixes)
        {
            var source = Path.Combine(store, OldDatabase + suffix);
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(store, NewDatabase + suffix));
            }
        }
    }

    // Moves what does not already exist at the destination; a folder present on both sides merges
    private static void Merge(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                if (Directory.Exists(dest))
                {
                    Merge(entry, dest);
                    if (!Directory.EnumerateFileSystemEntries(entry).Any())
                    {
                        Directory.Delete(entry);
                    }
                }
                else
                {
                    Directory.Move(entry, dest);
                }
            }
            else if (!File.Exists(dest))
            {
                File.Move(entry, dest);
            }
        }
    }
}
