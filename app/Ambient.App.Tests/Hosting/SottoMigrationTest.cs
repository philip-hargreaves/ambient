using Ambient.App.Core.Hosting;

namespace Ambient.App.Tests.Hosting;

public sealed class SottoMigrationTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ambient-migration-{Guid.NewGuid():N}");

    private string Old => Path.Combine(_root, "sotto");

    private string New => Path.Combine(_root, "ambient");

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
        Assert.False(SottoMigration.Run(Old, New), "nothing to migrate is not an error");
        Assert.False(Directory.Exists(New));

        Put(@"sotto\store\sotto.db", "db");
        Put(@"sotto\store\sotto.db-wal", "wal");
        Put(@"sotto\preferences.json", "{}");
        Put(@"sotto\dumps\a.dmp", "a");
        Put(@"ambient\dumps\b.dmp", "b");

        Assert.True(SottoMigration.Run(Old, New));

        Assert.Equal("db", Get(@"ambient\store\ambient.db"));
        Assert.Equal("wal", Get(@"ambient\store\ambient.db-wal"));
        Assert.Equal("{}", Get(@"ambient\preferences.json"));
        Assert.Equal("a", Get(@"ambient\dumps\a.dmp"));
        Assert.Equal("b", Get(@"ambient\dumps\b.dmp"));
        Assert.False(Directory.Exists(Old), "an emptied sotto folder is removed");
    }

    [Fact]
    public void AFileAlreadyAtTheDestinationIsNeverOverwritten()
    {
        Put(@"sotto\store\sotto.db", "the consultations");
        Put(@"ambient\store\ambient.db", "a stray engine run");
        Put(@"sotto\anchor.bin", "print");

        Assert.False(SottoMigration.Run(Old, New), "the blocked file stays where it can be found");

        Assert.Equal("a stray engine run", Get(@"ambient\store\ambient.db"));
        Assert.Equal("the consultations", Get(@"sotto\store\ambient.db"));
        Assert.Equal("print", Get(@"ambient\anchor.bin"));
    }
}
