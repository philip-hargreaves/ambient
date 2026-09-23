using Ambient.App.Core.Ports;

namespace Ambient.App.Tests.TestDoubles;

public sealed class FakeDialogService : IDialogService
{
    public bool Answer { get; set; } = true;

    public List<string> Confirmations { get; } = [];

    public Action? OnConfirm { get; set; }

    public bool EnrolmentOutcome { get; set; } = true;

    public Action? OnEnrolment { get; set; }

    public int EnrolmentsRun { get; private set; }

    public List<(string SessionId, string StartedAt)> ReflectionsShown { get; } = [];

    public Task<bool> ConfirmAsync(string title, string content, string primary, string cancel = "Cancel")
    {
        Confirmations.Add(title);
        OnConfirm?.Invoke();
        return Task.FromResult(Answer);
    }

    public Task<bool> RunEnrolmentAsync()
    {
        EnrolmentsRun++;
        OnEnrolment?.Invoke();
        return Task.FromResult(EnrolmentOutcome);
    }

    public Task ShowReflectionAsync(string sessionId, string startedAt)
    {
        ReflectionsShown.Add((sessionId, startedAt));
        return Task.CompletedTask;
    }
}

public sealed class FakeFilePicker : IFilePicker
{
    public string? SavePath { get; set; }

    public string? File { get; set; }

    public IReadOnlyList<string> Files { get; set; } = [];

    public List<string> SuggestedNames { get; } = [];

    public Task<string?> PickSaveAsync(string suggestedName, string typeLabel, string extension)
    {
        SuggestedNames.Add(suggestedName);
        return Task.FromResult(SavePath);
    }

    public Task<string?> PickFileAsync(string extension) => Task.FromResult(File);

    public Task<IReadOnlyList<string>> PickFilesAsync(IReadOnlyList<string> extensions) =>
        Task.FromResult(Files);
}

public sealed class FakeLauncher : ILauncher
{
    public bool LinksOpen { get; set; } = true;

    public List<string> Links { get; } = [];

    public List<string> Files { get; } = [];

    public List<string> Folders { get; } = [];

    public Task<bool> OpenLinkAsync(string link)
    {
        Links.Add(link);
        return Task.FromResult(LinksOpen);
    }

    public Task OpenFileAsync(string path)
    {
        Files.Add(path);
        return Task.CompletedTask;
    }

    public void RevealFolder(string path) => Folders.Add(path);
}

public sealed class FakeClipboard : IClipboard
{
    public bool Works { get; set; } = true;

    public List<string> Copied { get; } = [];

    public Task<bool> CopyAsync(string text)
    {
        Copied.Add(text);
        return Task.FromResult(Works);
    }
}

public sealed class FakeThemeService : IThemeService
{
    public List<string> Applied { get; } = [];

    public void Apply(string theme) => Applied.Add(theme);
}
