using Microsoft.Extensions.DependencyInjection;
using Ambient.App.Core.Composition;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Metrics;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

namespace Ambient.App.Tests.Composition;

/// <summary>The view-model graph over fakes, so a new constructor parameter fails here before launch.</summary>
public class CompositionTest
{
    private sealed class FakeHost : IEngineHost
    {
        public event Action<EngineStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public EngineStatus Status => EngineStatus.Stopped;

        public EngineFault? Fault => null;

        public int? EnginePid => null;

        public void Start()
        {
        }

        public void Shutdown()
        {
        }
    }

    private sealed class FixedMachine : IMachineInfoProvider
    {
        public MachineInfo Describe() => new("cpu", 32, "os", [], null);
    }

    [Fact]
    public void EveryRegisteredServiceResolves()
    {
        var engine = new FakeEngineClient(autoNotify: false);
        var dispatcher = new InlineDispatcher();
        var services = new ServiceCollection();
        services.AddSingleton<IEngineApi>(new EngineApi(engine));
        services.AddSingleton<IUiDispatcher>(dispatcher);
        services.AddSingleton<IDialogService, FakeDialogService>();
        services.AddSingleton<IFilePicker, FakeFilePicker>();
        services.AddSingleton<ILauncher, FakeLauncher>();
        services.AddSingleton<IClipboard, FakeClipboard>();
        services.AddSingleton<IThemeService, FakeThemeService>();
        services.AddSingleton<INavigationService, RecordingNavigationService>();
        services.AddSingleton<IEngineHost, FakeHost>();
        services.AddSingleton<IMachineInfoProvider, FixedMachine>();
        services.AddSingleton<IProcessMetrics, NoProcessMetrics>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AppPreferences(new MemoryPreferencesStore()));
        services.AddSingleton(new DemoMode());
        services.AddSingleton(sp => new PerformanceCollector(
            sp.GetRequiredService<IEngineApi>(), () => false, () => null,
            Path.Combine(Path.GetTempPath(), $"ambient-composition-{Guid.NewGuid():N}.jsonl")));
        services.AddSingleton(new StatusBarViewModel(new EngineApi(engine), dispatcher));
        services.AddCoreViewModels();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        foreach (var descriptor in services)
        {
            Assert.NotNull(provider.GetRequiredService(descriptor.ServiceType));
        }
    }
}
