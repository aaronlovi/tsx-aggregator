using System;
using System.Globalization;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

        return Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); })
            .ConfigureServices((context, services) => {

                var grpcPort = int.Parse(context.Configuration!.GetSection("Ports")["Grpc"] ?? DefaultPortStr, CultureInfo.InvariantCulture);
                services
                    .Configure<KestrelServerOptions>(opt =>
                    {
                        opt.ListenAnyIP(grpcPort, options => options.Protocols = HttpProtocols.Http2);
                        opt.AllowAlternateSchemes = true;
                    })
                    .Configure<GoogleCredentialsOptions>(context.Configuration.GetSection(GoogleCredentialsOptions.GoogleCredentials));
;

                // TODO: Add support for DbmInMemory if the connection string is not specified

                services
                    .AddHttpClient()
                    .AddSingleton<IValidateOptions<GoogleCredentialsOptions>, GoogleCredentialsOptionsValidator>()
                    .AddSingleton<PostgresExecutor>()
                    .AddSingleton<DbMigrations>()
                    .AddSingleton<IDbmService, DbmService>()
                    .AddSingleton<Registry>()
                    .AddSingleton<Aggregator>()
                    .AddSingleton<RawCollector>()
                    .AddSingleton<StockDataSvc>()
                    .AddSingleton<IStocksDataRequestsProcessor, StocksDataRequestsProcessor>()
                    .AddSingleton<IQuoteService, QuoteService>()
                    .AddSingleton<ISearchService, SearchService>()
                    .AddSingleton<IGoogleSheetsService, GoogleSheetsService>()
                    .AddHostedService(p => p.GetRequiredService<Aggregator>())
                    .AddHostedService(p => p.GetRequiredService<RawCollector>())
                    .AddHostedService(p => p.GetRequiredService<IStocksDataRequestsProcessor>())
                    .AddHostedService(p => p.GetRequiredService<IQuoteService>())
                    .AddHostedService(p => p.GetRequiredService<ISearchService>())
                    .AddGrpc();

                // Configures GoogleCredentialsOptions using the Options pattern
                // Validates the GoogleCredentialsOptions instance when the application starts.
                services
                    .AddOptions<GoogleCredentialsOptions>()
                    .Bind(context.Configuration.GetSection(GoogleCredentialsOptions.GoogleCredentials))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
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
    }
}
