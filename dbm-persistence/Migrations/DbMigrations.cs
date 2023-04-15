using System;
using EvolveDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace dbm_persistence;

public class DbMigrations {
    private readonly ILogger<DbMigrations> _logger;
    private readonly string _connectionString;

    public DbMigrations(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<DbMigrations>>();

        IConfiguration config = svp.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("tsx-scraper") ?? string.Empty;
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("Connection string is empty");
    }

    public void Up() {
        try {
            using var connection = new NpgsqlConnection(_connectionString);
            var evolve = new Evolve(connection, msg => _logger.LogInformation(msg)) {
                EmbeddedResourceAssemblies = new[] { typeof(DbMigrations).Assembly },
                // EmbeddedResourceFilters = new[] { "dbm-persistence" },
                Schemas = new[] { "public" },
                IsEraseDisabled = true,
                EnableClusterMode = true
            };
            evolve.Migrate();

            // Reload types to get proper introspection
            connection.Open();
            connection.ReloadTypes();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }
}
