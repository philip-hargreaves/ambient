namespace ClinicAVT.App.Core.Hosting;

/// <summary>
/// Moves the app's files out of a per-user folder named for an old product name. It moves item
/// by item and never overwrites, so a stray new folder cannot block the real data and a
/// failure part way leaves every file where it can be found.
/// </summary>
public static class FolderMigration
{
    public const string Database = "clinicavt.db";

    private static readonly string[] DatabaseSuffixes = ["", "-wal", "-shm"];

    /// <summary>The old per-user folders, oldest first, each with its store's file name.</summary>
    public static readonly IReadOnlyList<(string Folder, string Database)> Earlier =
        [("sotto", "sotto.db"), ("Ambient", "ambient.db")];

    /// <summary>True when the old folder existed, ended up empty and was deleted.</summary>
    public static bool Run(string oldRoot, string newRoot, string? oldDatabase = null)
    {
        if (!Directory.Exists(oldRoot))
        {
            return false;
        }

        if (oldDatabase is not null)
        {
            RenameDatabase(Path.Combine(oldRoot, "store"), oldDatabase);
        }

        Merge(oldRoot, newRoot);
        if (Directory.EnumerateFileSystemEntries(oldRoot).Any())
        {
            return false;
        }

        Directory.Delete(oldRoot);
        return true;
    }

    // The database and its journal files rename together
    private static void RenameDatabase(string store, string oldDatabase)
    {
        if (!File.Exists(Path.Combine(store, oldDatabase)))
        {
            return;
        }

        foreach (var suffix in DatabaseSuffixes)
        {
            var source = Path.Combine(store, oldDatabase + suffix);
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(store, Database + suffix));
            }
        }
    }

    // Moves each entry missing at the destination. A folder present on both sides merges
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
