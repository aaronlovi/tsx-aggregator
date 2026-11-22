using System;
using System.Globalization;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;

namespace tsx_aggregator;

public class Program {
    private const int DefaultGrpcPort = 7001;
    internal const string GrpcPortKey = "Ports:Grpc";
    internal const string GrpcListenOnAllInterfacesKey = "Ports:GrpcListenOnAllInterfaces";

    /// <summary>
    /// The gRPC port and whether it listens on every network interface (<c>Ports:GrpcListenOnAllInterfaces</c>,
    /// default false: localhost only). Throws on a value that is not a port number or a boolean.
    /// </summary>
    internal static GrpcListener GetGrpcListener(IConfiguration configuration) {
        int port = DefaultGrpcPort;
        string? portValue = configuration[GrpcPortKey];
        if (portValue is not null
            && (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            throw new InvalidOperationException($"{GrpcPortKey} must be a port number from 1 to 65535, got '{portValue}'");

        bool allInterfaces = false;
        string? allValue = configuration[GrpcListenOnAllInterfacesKey];
        if (allValue is not null && !bool.TryParse(allValue, out allInterfaces))
            throw new InvalidOperationException($"{GrpcListenOnAllInterfacesKey} must be true or false, got '{allValue}'");

        return new GrpcListener(port, allInterfaces);
    }

    public static async Task<int> Main(string[] args) {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();
        var log = Log.Logger.ForContext("Startup", true);

        try {
            log.Information("Building the host");
            var host = BuildHost<Startup>(args);
            log.Information("Running the host");
            await host.RunAsync();
            log.Information("Service process gracefully completed");
            return 0;
        } catch (Exception ex) {
            log.Error(ex, "Service execution is terminated with an error");
            return -1;
        } finally {
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildHost<TStartup>(string[] args) where TStartup : class {

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, cfg) =>
                cfg.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true))
            .ConfigureWebHostDefaults(webBuilder => { _ = webBuilder.UseStartup<TStartup>(); })
            .ConfigureServices((context, services) => {

                GrpcListener grpcListener = GetGrpcListener(context.Configuration!);
                _ = services
                    .Configure<KestrelServerOptions>(opt => {
                        // Localhost by default: the AdminService (pause, requeue) has no authentication.
                        if (grpcListener.AllInterfaces)
                            opt.ListenAnyIP(grpcListener.Port, options => options.Protocols = HttpProtocols.Http2);
                        else
                            opt.ListenLocalhost(grpcListener.Port, options => options.Protocols = HttpProtocols.Http2);
                        opt.AllowAlternateSchemes = true;
                    })
                    .Configure<GoogleCredentialsOptions>(context.Configuration.GetSection(GoogleCredentialsOptions.GoogleCredentials))
                    .Configure<HostedServicesOptions>(context.Configuration.GetSection(HostedServicesOptions.HostedServices))
                    .Configure<AlertSettingsOptions>(context.Configuration.GetSection(AlertSettingsOptions.AlertSettings))
                    .Configure<ScoringOptions>(context.Configuration.GetSection(ScoringOptions.Scoring))
                    .Configure<ScraperOptions>(context.Configuration.GetSection(ScraperOptions.Scraper));

                // Using the Options pattern, validate configuration instances that are mapped
                // from configuration when the application starts.
                _ = services
                    .AddValidatedOptions<GoogleCredentialsOptions>(context.Configuration, GoogleCredentialsOptions.GoogleCredentials)
                    .AddValidatedOptions<HostedServicesOptions>(context.Configuration, HostedServicesOptions.HostedServices);

                _ = services
                    .AddHttpClient()
                    .AddSingleton<IValidateOptions<GoogleCredentialsOptions>, GoogleCredentialsOptionsValidator>()
                    .AddSingleton<PostgresExecutor>()
                    .AddSingleton<DbMigrations>()
                    .AddSingleton<Registry>()
                    .AddSingleton<Aggregator>()
                    .AddSingleton<RawCollector>()
                    .AddSingleton<StockDataSvc>()
                    .AddSingleton<IStocksDataRequestsProcessor, StocksDataRequestsProcessor>()
                    .AddSingleton<IQuoteService, QuoteService>()
                    .AddSingleton<ISearchService, SearchService>()
                    .AddSingleton<IGoogleSheetsService, GoogleSheetsService>()
                    .AddSingleton<IEmailService, EmailService>()
                    .AddSingleton<TopScoreAlertService>();

                // Pausable services, for AdminSvc. Same singletons as above.
                _ = services
                    .AddSingleton<IPausableService>(p => p.GetRequiredService<Aggregator>())
                    .AddSingleton<IPausableService>(p => p.GetRequiredService<RawCollector>())
                    .AddSingleton<IEventQueueWaker>(p => p.GetRequiredService<Aggregator>());

                if (DoesConfigContainConnectionString(context.Configuration))
                    _ = services.AddSingleton<IDbmService, DbmService>();
                else
                    _ = services.AddSingleton<IDbmService, DbmInMemory>();

                var serviceProvider = services.BuildServiceProvider();
                var hostedServicesOptions = serviceProvider.GetRequiredService<IOptions<HostedServicesOptions>>().Value;

                // Always first: hosted services start in registration order, and the services below
                // wait for the registry that this loads.
                _ = services.AddHostedService<RegistryLoader>();

                if (hostedServicesOptions.RunAggregator ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<Aggregator>());

                if (hostedServicesOptions.RunRawCollector ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<RawCollector>());

                if (hostedServicesOptions.RunStocksDataRequestsProcessor ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<IStocksDataRequestsProcessor>());

                if (hostedServicesOptions.RunQuoteService ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<IQuoteService>());

                if (hostedServicesOptions.RunSearchService ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<ISearchService>());

                if (hostedServicesOptions.RunTopScoreAlertService ?? false)
                    _ = services.AddHostedService(p => p.GetRequiredService<TopScoreAlertService>());

                _ = services.AddGrpc();
            })
            .ConfigureLogging(builder => {

                Log.Logger = new LoggerConfiguration()
                    .WriteTo.Console()
                    .WriteTo.File(
                        "tsx-aggregator-.txt",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Properties:j} {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                _ = builder
                    .ClearProviders()
                    .AddSerilog(Log.Logger);
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        LogConfig(host.Services.GetRequiredService<IConfiguration>(), logger);

        // Bad scoring settings would silently change every score: refuse to start.
        if (host.Services.GetRequiredService<IOptions<ScoringOptions>>().Value.Validate() is string scoringError)
            throw new InvalidOperationException(scoringError);

        // A wrong browser path would only show up as failed scrapes: refuse to start.
        if (host.Services.GetRequiredService<IOptions<ScraperOptions>>().Value.Validate() is string scraperError)
            throw new InvalidOperationException(scraperError);

        return host;
    }

    private static bool DoesConfigContainConnectionString(IConfiguration configuration)
        => configuration.GetConnectionString(IDbmService.TsxScraperConnStringName) is not null;

    private static void LogConfig(IConfiguration config, Microsoft.Extensions.Logging.ILogger logger) {
        logger.LogInformation("==========BEGIN CRITICAL CONFIGURATION==========");
        LogConfigSection(config, logger, GoogleCredentialsOptions.GoogleCredentials);
        LogConfigSection(config, logger, HostedServicesOptions.HostedServices);
        LogConfigSection(config, logger, AlertSettingsOptions.AlertSettings);
        LogConfigSection(config, logger, ScoringOptions.Scoring);
        LogConfigSection(config, logger, ScraperOptions.Scraper);
        logger.LogInformation("==========END CRITICAL CONFIGURATION==========");
    }

    private static void LogConfigSection(
        IConfiguration config,
        Microsoft.Extensions.Logging.ILogger logger,
        string section) {
        foreach (var child in config.GetSection(section).GetChildren())
            logger.LogInformation("[{Section}] {Key} = {Value}", section, child.Key, FormatConfigValueForLog(child.Key, child.Value));
    }

    private static readonly string[] SecretKeyParts = ["password", "secret", "token", "apikey"];

    /// <summary>
    /// A configuration value as the startup log shows it: secrets (passwords, secrets, tokens, API keys) only say
    /// whether they are set, since the log also goes to a file.
    /// </summary>
    internal static string FormatConfigValueForLog(string key, string? value) {
        bool isSecret = Array.Exists(SecretKeyParts, part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
        if (!isSecret)
            return value ?? string.Empty;
        return string.IsNullOrEmpty(value) ? "(not set)" : "(set, masked)";
    }
}

/// <summary>Where tsx-aggregator's gRPC server listens.</summary>
internal sealed record GrpcListener(int Port, bool AllInterfaces);
