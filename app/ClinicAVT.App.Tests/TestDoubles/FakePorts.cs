using ClinicAVT.App.Core.Ports;

namespace ClinicAVT.App.Tests.TestDoubles;

public sealed class FakeDialogService : IDialogService
{
    public bool Answer { get; set; } = true;

    public Action? OnConfirm { get; set; }

    public Action? OnEnrolment { get; set; }

    public Task<bool> ConfirmAsync(string title, string content, string primary, string cancel = "Cancel")
    {
        OnConfirm?.Invoke();
        return Task.FromResult(Answer);
    }

    public Task<bool> RunEnrolmentAsync()
    {
        OnEnrolment?.Invoke();
        return Task.FromResult(true);
    }

    public Task ShowReflectionAsync(string sessionId, string startedAt) => Task.CompletedTask;
}

public sealed class FakeFilePicker : IFilePicker
{
    public string? SavePath { get; set; }

    public IReadOnlyList<string> Files { get; set; } = [];

    public List<string> SuggestedNames { get; } = [];

    public Task<string?> PickSaveAsync(string suggestedName, string typeLabel, string extension)
    {
        SuggestedNames.Add(suggestedName);
        return Task.FromResult(SavePath);
    }

    public Task<string?> PickFileAsync(string extension) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PickFilesAsync(IReadOnlyList<string> extensions) =>
        Task.FromResult(Files);
}

public sealed class FakeLauncher : ILauncher
{
    public List<string> Files { get; } = [];

    public Task<bool> OpenLinkAsync(string link) => Task.FromResult(true);

    public Task OpenFileAsync(string path)
    {
        Files.Add(path);
        return Task.CompletedTask;
    }

    public void RevealFolder(string path)
    {
    }
}

public sealed class FakeClipboard : IClipboard
{
    public List<string> Copied { get; } = [];

    public Task<bool> CopyAsync(string text)
    {
        Copied.Add(text);
        return Task.FromResult(true);
    }
}

public sealed class FakeThemeService : IThemeService
{
    public List<string> Applied { get; } = [];

    public void Apply(string theme) => Applied.Add(theme);
}
