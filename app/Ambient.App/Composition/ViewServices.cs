using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Appraisal;
using Ambient.App.Features.Consultation;
using Ambient.App.Features.Demo;
using Ambient.App.Features.Documents;
using Ambient.App.Features.Guidance;
using Ambient.App.Features.Help;
using Ambient.App.Features.Sessions;
using Ambient.App.Features.Settings;
using Ambient.App.Platform;
using Ambient.App.Shell;

namespace Ambient.App.Composition;

/// <summary>
/// The views. Pages live for the app: the navigation service keeps them on its back stack,
/// so their subscriptions to the singleton view models are for the app's lifetime too. The
/// views inside a page are transient, one set per page, since an element has one parent.
/// </summary>
internal static class ViewServices
{
    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<SessionControlsView>();
        services.AddTransient<DemoTrayView>();
        services.AddTransient<TranscriptPaneView>();
        services.AddTransient<GuidanceSectionView>();
        services.AddTransient<PageView>();
        services.AddTransient<NoteEditorView>();
        services.AddTransient<PatientEditorView>();
        services.AddTransient<ReviewSurfaceView>();
        services.AddTransient<StatusBarView>();
        services.AddSingleton<ConsultationView>();
        services.AddSingleton<SessionsView>();
        services.AddSingleton<AppraisalsView>();
        services.AddSingleton<SettingsView>();
        services.AddSingleton<HelpView>();
        // The platform adapters reach the window through the accessor from the moment it exists
        services.AddSingleton(sp =>
        {
            var window = ActivatorUtilities.CreateInstance<MainWindow>(sp);
            sp.GetRequiredService<WindowAccessor>().Window = window;
            return window;
        });
        services.AddSingleton<AppShutdown>();
        // The one place a page is looked up by name
        services.AddSingleton(sp => new NavigationService(new Dictionary<string, Func<UIElement>>
        {
            [Routes.Consultation] = sp.GetRequiredService<ConsultationView>,
            [Routes.Sessions] = sp.GetRequiredService<SessionsView>,
            [Routes.Appraisals] = sp.GetRequiredService<AppraisalsView>,
            [Routes.Help] = sp.GetRequiredService<HelpView>,
            [Routes.Settings] = sp.GetRequiredService<SettingsView>,
        }));
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        return services;
    }
}
