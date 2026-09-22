namespace Ambient.App.Core.Features.Demo;

/// <summary>A written case that can stand in as the clinical note on a demo record.</summary>
public sealed record DemoCase(string Title, string Text);

public static class DemoCases
{
    /// <summary>
    /// Loads demo/cases.txt, probing upward like the tracks manifest: blank-line
    /// separated blocks, a title line, an optional dashed underline, then the case.
    /// Missing or empty means none.
    /// </summary>
    public static IReadOnlyList<DemoCase> Load(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++, dir = Directory.GetParent(dir)?.FullName)
        {
            var file = Path.Combine(dir, "demo", "cases.txt");
            if (File.Exists(file))
            {
                try
                {
                    return Parse(File.ReadAllText(file));
                }
                catch (Exception)
                {
                    return [];
                }
            }
        }

        return [];
    }

    public static IReadOnlyList<DemoCase> Parse(string text)
    {
        var cases = new List<DemoCase>();
        foreach (var block in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Trim().Length > 0 && l.Trim().Any(c => c != '-'))
                .Select(l => l.Trim())
                .ToList();
            if (lines.Count >= 2)
            {
                cases.Add(new DemoCase(lines[0], string.Join(" ", lines.Skip(1))));
            }
        }

        return cases;
    }
}
