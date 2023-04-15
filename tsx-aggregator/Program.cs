using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using tsx_aggregator.Raw;

namespace tsx_aggregator;

public class Program {
    public static void Main(string[] args) {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services => {
                _ = services.AddHostedService<Aggregator>();
                _ = services.AddHostedService<RawCollector>();
                _ = services.AddSingleton<PostgresExecutor>();

                _ = services.AddSingleton<DbMigrations>();

                // TODO: Add support for DbmInMemory if the connection string is not specified
                _ = services.AddSingleton<IDbmService, DbmService>();
                _ = services.AddSingleton<Registry>();

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

        host.Run();
    }
}
