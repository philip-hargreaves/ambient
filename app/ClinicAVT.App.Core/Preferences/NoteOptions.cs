namespace ClinicAVT.App.Core.Preferences;

/// <summary>The note options the engine accepts, in the order the combos list them.</summary>
public static class NoteOptions
{
    public static readonly IReadOnlyList<NoteOption> Styles =
        [new("prose", "Prose"), new("soap", "SOAP")];

    public static readonly IReadOnlyList<NoteOption> Details =
        [new("concise", "Concise"), new("standard", "Standard"), new("detailed", "Detailed")];

    public static NoteOption DefaultStyle => Styles[0];

    public static NoteOption DefaultDetail => Details[1];

    /// <summary>The style with that engine value, the default for anything else.</summary>
    public static NoteOption Style(string? value) => Find(Styles, value) ?? DefaultStyle;

    public static NoteOption Detail(string? value) => Find(Details, value) ?? DefaultDetail;

    /// <summary>The option's place in the list, -1 when unknown.</summary>
    public static int IndexOf(IReadOnlyList<NoteOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Value == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static NoteOption? Find(IReadOnlyList<NoteOption> options, string? value) =>
        value is null ? null : options.FirstOrDefault(o => o.Value == value);
}
