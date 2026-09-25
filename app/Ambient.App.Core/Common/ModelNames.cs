using System.Text.RegularExpressions;

namespace Ambient.App.Core.Common;

/// <summary>Model ids as the chips and combos name them.</summary>
public static class ModelNames
{
    /// <summary>
    /// A manifest without a display name: "whisper-turbo-int8" reads as "Whisper Turbo".
    /// The precision suffix is dropped, size tokens kept.
    /// </summary>
    public static string Friendly(string id)
    {
        var words = id.Split('-')
            .Where(t => t is not ("int8" or "int4" or "fp16" or "fp32"))
            .Select(t => Regex.IsMatch(t, @"^\d+b$")
                ? t.ToUpperInvariant()
                : char.ToUpperInvariant(t[0]) + t[1..]);
        return string.Join(' ', words);
    }
}
