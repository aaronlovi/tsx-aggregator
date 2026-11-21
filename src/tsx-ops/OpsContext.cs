using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace tsx_ops;

/// <summary>Process exit codes for tsx-ops commands.</summary>
internal static class ExitCodes {
    public const int Ok = 0;
    public const int Error = 1;
    public const int Usage = 2;
}

/// <summary>Thrown for bad arguments or configuration. Reported with the command's usage and exit code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Configuration and logging shared by all commands.
/// Configuration order (later wins): appsettings.json, appsettings.{TSX_OPS_ENVIRONMENT}.json, environment variables.
/// Logs go to stderr so stdout carries only command output.
/// </summary>
internal sealed class OpsContext : IDisposable {
    public const string EnvironmentVariableName = "TSX_OPS_ENVIRONMENT";
    public const string ConnectionStringName = "tsx-scraper";
    public const string ApplicationName = "tsx-ops";
    private const int DefaultStatementTimeoutSeconds = 120;

    private OpsContext(IConfiguration configuration, ILoggerFactory loggerFactory, TextWriter output) {
        Configuration = configuration;
        LoggerFactory = loggerFactory;
        Output = output;
    }

    public IConfiguration Configuration { get; }
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>Where command output (data, reports) is written. stdout in normal use.</summary>
    public TextWriter Output { get; }

    public int StatementTimeoutSeconds =>
        Configuration.GetValue("Ops:StatementTimeoutSeconds", DefaultStatementTimeoutSeconds);

    public static OpsContext Create() {
        string? environment = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true);
        if (!string.IsNullOrWhiteSpace(environment))
            builder = builder.AddJsonFile($"appsettings.{environment}.json", optional: true);
        IConfiguration configuration = builder.AddEnvironmentVariables().Build();

        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(o => {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            })
            .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));

        return new OpsContext(configuration, loggerFactory, Console.Out);
    }

    public ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();

    /// <summary>
    /// The database connection string: <paramref name="overrideValue"/> if given, otherwise ConnectionStrings:tsx-scraper,
    /// with the application name and a command timeout just above the statement timeout.
    /// </summary>
    public string GetConnectionString(string? overrideValue) {
        string? configured = overrideValue ?? Configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(configured)) {
            throw new UsageException(
                $"No connection string. Set ConnectionStrings__{ConnectionStringName}, add it to appsettings, or pass --connection.");
        }

        var builder = new NpgsqlConnectionStringBuilder(configured) {
            ApplicationName = ApplicationName,
            CommandTimeout = StatementTimeoutSeconds + 10,
        };
        return builder.ConnectionString;
    }

    public void Dispose() => LoggerFactory.Dispose();
}
