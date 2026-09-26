using ClinicAVT.App.Core.Hosting;

namespace ClinicAVT.App.Tests.Hosting;

public sealed class FolderMigrationTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clinicavt-migration-{Guid.NewGuid():N}");

    private string Old => Path.Combine(_root, "sotto");

    private string New => Path.Combine(_root, "ClinicAVT");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Get(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    [Fact]
    public void TheStoreMovesUnderItsNewNameWithItsJournalAndFoldersOnBothSidesMerge()
    {
        Assert.False(FolderMigration.Run(Old, New, "sotto.db"), "nothing to migrate is not an error");
        Assert.False(Directory.Exists(New));

        Put(@"sotto\store\sotto.db", "db");
        Put(@"sotto\store\sotto.db-wal", "wal");
        Put(@"sotto\preferences.json", "{}");
        Put(@"sotto\dumps\a.dmp", "a");
        Put(@"ClinicAVT\dumps\b.dmp", "b");

        Assert.True(FolderMigration.Run(Old, New, "sotto.db"));

        Assert.Equal("db", Get(@"ClinicAVT\store\clinicavt.db"));
        Assert.Equal("wal", Get(@"ClinicAVT\store\clinicavt.db-wal"));
        Assert.Equal("{}", Get(@"ClinicAVT\preferences.json"));
        Assert.Equal("a", Get(@"ClinicAVT\dumps\a.dmp"));
        Assert.Equal("b", Get(@"ClinicAVT\dumps\b.dmp"));
        Assert.False(Directory.Exists(Old), "an emptied sotto folder is removed");

        // An Ambient folder merges the same way. Its store cannot land on the one already
        // there, so it stays behind where it can be found
        Put(@"Ambient\store\ambient.db", "later db");
        Put(@"Ambient\masters.json", "[]");
        Assert.False(FolderMigration.Run(Path.Combine(_root, "Ambient"), New, "ambient.db"));
        Assert.Equal("db", Get(@"ClinicAVT\store\clinicavt.db"));
        Assert.Equal("[]", Get(@"ClinicAVT\masters.json"));
        Assert.Equal("later db", Get(@"Ambient\store\clinicavt.db"));
    }

    [Fact]
    public void AnAmbientFolderAloneMovesWholeWithItsStoreRenamed()
    {
        Put(@"Ambient\store\ambient.db", "db");
        Put(@"Ambient\store\ambient.db-shm", "shm");
        Put(@"Ambient\preferences.json", "{}");

        Assert.True(FolderMigration.Run(Path.Combine(_root, "Ambient"), New, "ambient.db"));

        Assert.Equal("db", Get(@"ClinicAVT\store\clinicavt.db"));
        Assert.Equal("shm", Get(@"ClinicAVT\store\clinicavt.db-shm"));
        Assert.Equal("{}", Get(@"ClinicAVT\preferences.json"));
        Assert.False(Directory.Exists(Path.Combine(_root, "Ambient")));
    }

    [Fact]
    public void AFileAlreadyAtTheDestinationIsNeverOverwritten()
    {
        Put(@"sotto\store\sotto.db", "the consultations");
        Put(@"ClinicAVT\store\clinicavt.db", "a stray engine run");
        Put(@"sotto\anchor.bin", "print");

        Assert.False(FolderMigration.Run(Old, New, "sotto.db"), "the blocked file stays where it can be found");

        Assert.Equal("a stray engine run", Get(@"ClinicAVT\store\clinicavt.db"));
        Assert.Equal("the consultations", Get(@"sotto\store\clinicavt.db"));
        Assert.Equal("print", Get(@"ClinicAVT\anchor.bin"));
    }
}
