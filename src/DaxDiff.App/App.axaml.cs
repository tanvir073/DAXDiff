using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DaxDiff.App.Services;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Desktop;
using DaxDiff.Presentation.Services;
using DaxDiff.Presentation.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DaxDiff.App;

public partial class App : Avalonia.Application
{
    private IHost? _host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var args = desktop.Args ?? [];
        Directory.CreateDirectory(AppPaths.Logs);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(AppPaths.Logs, "daxdiff-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = args,
        });
        builder.Configuration.AddJsonFile(AppPaths.UserSettings, optional: true, reloadOnChange: false);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(serilog, dispose: true);
        ConfigureServices(builder.Services, builder.Configuration);
        _host = builder.Build();

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            _host.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled UI exception");
            _host.Services.GetService<IDialogService>()?.ShowError(ErrorClassifier.Classify(e.Exception, "DAXDiff"));
            e.Handled = true;
        };

        // The shared view models switch themes through this; lists use the toolkit-neutral FilteredListView.
        ThemeCatalog.Applier = ThemeBrushes.Apply;
        // Before any window or view model reads localized text.
        LanguageService.Apply(builder.Configuration["Appearance:Language"]);
        ThemeCatalog.Apply(builder.Configuration["Appearance:Theme"]);

        var main = _host.Services.GetRequiredService<MainViewModel>();
        // Let the current UI event (such as a settings change) finish before the app closes.
        main.ShutdownRequested += (_, _) => Dispatcher.UIThread.Post(() => desktop.Shutdown());
        var window = new MainWindow { DataContext = main };
        _host.Services.GetRequiredService<SourceSessionFactory>().ParentWindow = () =>
            OperatingSystem.IsWindows() ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero : IntPtr.Zero;
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => _host.Dispose();
        window.Opened += (_, _) =>
        {
            main.ApplyLaunchArguments(LaunchArguments.Parse(args));
            _host.Services.GetRequiredService<ILogger<App>>().LogInformation("DAXDiff {Version} started on {Os}", ComparisonRunner.AppVersion, ProductLinks.CurrentOs);
        };

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var authOptions = new AuthOptions
        {
            ClientId = configuration["AzureAd:ClientId"] ?? "",
            TenantId = configuration["AzureAd:TenantId"] is { Length: > 0 } tenant ? tenant : "organizations",
            RedirectUri = configuration["AzureAd:RedirectUri"] ?? "http://localhost",
            PowerBiApiBase = configuration["PowerBi:ApiBase"] ?? new AuthOptions().PowerBiApiBase,
            XmlaEndpointBase = configuration["PowerBi:XmlaEndpointBase"] ?? new AuthOptions().XmlaEndpointBase,
        };
        var synonyms = configuration.GetSection("Matching:Synonyms").GetChildren()
            .Select(group => group.GetChildren().Select(s => s.Value ?? "").ToList())
            .ToList();

        services.AddSingleton(authOptions);
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<SourceSessionFactory>();
        services.AddSingleton<IFilterEditorService, FilterEditorService>();
        services.AddSingleton<DesktopSessionLocator>();
        services.AddSingleton<IConnectorFactory, ConnectorFactory>();
        services.AddSingleton<ComparisonRunner>();
        services.AddSingleton(new AutoMatcher(synonyms));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<RecentProjects>();
        services.AddSingleton<MainViewModel>();
    }
}
