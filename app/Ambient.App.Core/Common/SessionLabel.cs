using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Common;

/// <summary>The consultation's label as retyped on any page.</summary>
public static class SessionLabel
{
    /// <summary>
    /// Saves the typed title when it is new. Returns the label now saved: the typed one, or the
    /// old one when the title was blank, unchanged or refused.
    /// </summary>
    public static async Task<string> SaveAsync(
        IEngineApi engine, StatusBarViewModel status, string id, string typed, string saved)
    {
        var title = typed.Trim();
        if (id.Length == 0 || title.Length == 0 || title == saved)
        {
            return saved;
        }

        return await EngineCall.ReportAsync(status, "could not rename", () => engine.LabelSessionAsync(id, title))
            .ConfigureAwait(true)
            ? title
            : saved;
    }
}
