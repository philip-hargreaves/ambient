using ClinicAVT.App.Core.Hosting;

namespace ClinicAVT.App.Composition;

/// <summary>
/// Moves the per-user files of former product names into this one's folders. It runs before
/// the service container exists. A service opened first would find an empty folder and later
/// save its defaults over the moved file.
/// </summary>
public static class EarlierNames
{
    public static IReadOnlyList<(string From, string To)> Migrate(AppPaths paths)
    {
        var moved = new List<(string, string)>();
        foreach (var (folder, database) in FolderMigration.Earlier)
        {
            if (FolderMigration.Run(paths.Sibling(folder), paths.LocalState, database))
            {
                moved.Add((folder, paths.LocalState));
            }
        }

        var guidelines = AppPaths.Guidelines("ClinicAVT");
        if (FolderMigration.Run(AppPaths.Guidelines("Ambient"), guidelines))
        {
            moved.Add(("Ambient guidelines", guidelines));
        }

        return moved;
    }
}
