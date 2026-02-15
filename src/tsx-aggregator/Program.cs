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
    private const string DefaultPortStr = "7001";
    
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
            .ConfigureWebHostDefaults(webBuilder => { _ = webBuilder.UseStartup<TStartup>(); })
            .ConfigureServices((context, services) => {

                var grpcPort = int.Parse(context.Configuration!.GetSection("Ports")["Grpc"] ?? DefaultPortStr, CultureInfo.InvariantCulture);
                _ = services
                    .Configure<KestrelServerOptions>(opt => {
                        opt.ListenAnyIP(grpcPort, options => options.Protocols = HttpProtocols.Http2);
                        opt.AllowAlternateSchemes = true;
                    })
                    .Configure<GoogleCredentialsOptions>(context.Configuration.GetSection(GoogleCredentialsOptions.GoogleCredentials))
                    .Configure<HostedServicesOptions>(context.Configuration.GetSection(HostedServicesOptions.HostedServices));

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
                    .AddSingleton<IGoogleSheetsService, GoogleSheetsService>();

                if (DoesConfigContainConnectionString(context.Configuration))
                    _ = services.AddSingleton<IDbmService, DbmService>();
                else
                    _ = services.AddSingleton<IDbmService, DbmInMemory>();

                var serviceProvider = services.BuildServiceProvider();
                var hostedServicesOptions = serviceProvider.GetRequiredService<IOptions<HostedServicesOptions>>().Value;

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

        return host;
    }

    private static bool DoesConfigContainConnectionString(IConfiguration configuration)
        => configuration.GetConnectionString(IDbmService.TsxScraperConnStringName) is not null;

    private static void LogConfig(IConfiguration config, Microsoft.Extensions.Logging.ILogger logger) {
        logger.LogInformation("==========BEGIN CRITICAL CONFIGURATION==========");
        LogConfigSection(config, logger, GoogleCredentialsOptions.GoogleCredentials);
        LogConfigSection(config, logger, HostedServicesOptions.HostedServices);
        logger.LogInformation("==========END CRITICAL CONFIGURATION==========");
    }

    private static void LogConfigSection(
        IConfiguration config,
        Microsoft.Extensions.Logging.ILogger logger,
        string section) {
        foreach (var child in config.GetSection(section).GetChildren())
            logger.LogInformation("[{Section}] {Key} = {Value}", section, child.Key, child.Value);
    }
}
