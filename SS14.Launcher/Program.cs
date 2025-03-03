using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.ReactiveUI;
using Serilog;
using Splat;
using SS14.Launcher.Models;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.EngineManager;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.ViewModels;
using SS14.Launcher.Views;
using LogEventLevel = Serilog.Events.LogEventLevel;

namespace SS14.Launcher;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static void Main(string[] args)
    {
        VcRedistCheck.Check();

        var cfg = new DataManager();
        cfg.Load();
        Locator.CurrentMutable.RegisterConstant(cfg);

        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(LauncherVersion.Name, LauncherVersion.Version?.ToString()));
        http.DefaultRequestHeaders.Add("SS14-Launcher-Fingerprint", cfg.Fingerprint.ToString());
        Locator.CurrentMutable.RegisterConstant(http);

        LauncherPaths.CreateDirs();

        var logCfg = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console();

        if (cfg.LogLauncher)
        {
            logCfg = logCfg.WriteTo.File(LauncherPaths.PathLauncherLog);
        }

        Log.Logger = logCfg.CreateLogger();

#if DEBUG
        Logger.Sink = new AvaloniaSeriLogger(new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Area}] {Message} ({SourceType} #{SourceHash})\n")
            .CreateLogger());
#endif

        BuildAvaloniaApp().Start(AppMain, args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI();

    // Your application's entry point. Here you can initialize your MVVM framework, DI
    // container, etc.
    private static void AppMain(Application app, string[] args)
    {
        var cfg = Locator.Current.GetService<DataManager>();
        Locator.CurrentMutable.RegisterConstant<IEngineManager>(new EngineManagerDynamic());
        Locator.CurrentMutable.RegisterConstant(new ServerStatusCache());
        Locator.CurrentMutable.RegisterConstant(new Updater());
        Locator.CurrentMutable.RegisterConstant(new AuthApi());
        Locator.CurrentMutable.RegisterConstant(new LoginManager());

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel
        };
        viewModel.OnWindowInitialized();

        app.Run(window);
    }
}