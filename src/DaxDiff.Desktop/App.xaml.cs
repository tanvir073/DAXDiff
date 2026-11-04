using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Desktop;
using DaxDiff.Connectors.Service;
using DaxDiff.Desktop.Services;
using DaxDiff.Desktop.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DaxDiff.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF formats bindings as en-US unless told otherwise; use the user's regional settings.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));

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
            Args = e.Args,
        });
        builder.Configuration.AddJsonFile(AppPaths.UserSettings, optional: true, reloadOnChange: false);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(serilog, dispose: true);
        ConfigureServices(builder.Services, builder.Configuration);
        _host = builder.Build();

        DispatcherUnhandledException += OnUnhandledException;
        // The shared view models filter lists and switch themes through these.
        ListViews.Create = source => new WpfListView(source);
        ThemeCatalog.Applier = ThemeService.Apply;
        // Before any window or view model reads localized text.
        LanguageService.Apply(builder.Configuration["Appearance:Language"]);
        ThemeCatalog.Apply(builder.Configuration["Appearance:Theme"]);

        var main = _host.Services.GetRequiredService<MainViewModel>();
        // Let the current UI event (such as a settings change) finish before the app closes.
        main.ShutdownRequested += (_, _) => Dispatcher.BeginInvoke(Shutdown);
        var window = new MainWindow { DataContext = main };
        _host.Services.GetRequiredService<SourceSessionFactory>().ParentWindow = () => new System.Windows.Interop.WindowInteropHelper(window).Handle;
        MainWindow = window;
        window.Show();
        main.ApplyLaunchArguments(LaunchArguments.Parse(e.Args));
        _host.Services.GetRequiredService<ILogger<App>>().LogInformation("DAXDiff {Version} started", ComparisonRunner.AppVersion);
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

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled UI exception");
        _host?.Services.GetService<IDialogService>()?.ShowError(ErrorClassifier.Classify(e.Exception, "DAXDiff"));
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
