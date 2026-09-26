using ClinicAVT.App.Core.Shell;
using ClinicAVT.Client;

namespace ClinicAVT.App.Core.Common;

public static class SessionLabel
{
    /// <summary>
    /// Saves the typed title when it is new. Returns the saved label, which stays the old one
    /// when the title is blank, unchanged or refused.
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
