using AgentDemo.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace AgentDemo.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Single user, single webview: scoped services here live for the whole app.
        builder.Services.AddSingleton<ILocalStore, FileLocalStore>();
        builder.Services.AddSingleton<AppLifecycle>();
        builder.Services.AddSingleton<IProfileService, ProfileService>();
        // Reuses the UI's JS audio (_content/AgentDemo.UI/audio.js) inside the WebView.
        builder.Services.AddScoped<ISoundService, SoundService>();

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android => android
                .OnPause(activity => _ = Services?.GetService<AppLifecycle>()?.PauseAsync())
                .OnResume(activity => _ = Services?.GetService<AppLifecycle>()?.ResumeAsync()));
#endif
        });

        return builder.Build();
    }

    internal static IServiceProvider? Services => IPlatformApplication.Current?.Services;
}
