using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Common;

public static class ClipboardExtensions
{
    /// <summary>Copies and reports the outcome on the status line, naming what was copied.</summary>
    public static async Task CopyAsync(
        this IClipboard clipboard, StatusBarViewModel status, string text, string what)
    {
        status.Append(await clipboard.CopyAsync(text).ConfigureAwait(true)
            ? $"{what} copied"
            : "Copy failed - the clipboard is unavailable");
    }
}
