using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace tsx_aggregator;

public class Startup {
    private readonly IConfiguration _config;
    
    public Startup(IConfiguration config) {
        _config = config;
    }

    public void ConfigureServices(IServiceCollection services) {
        services.AddGrpc();

        VerifyCriticalConfiguration();

        // project-specific configurations
    }

    private void VerifyCriticalConfiguration() {
        var connectionString = _config.GetConnectionString("tsx-scraper");
        if (string.IsNullOrEmpty(connectionString))
            throw new Exception("Missing 'tsx-scraper' connection string in app configuration");

        var databaseSchema = _config["DatabaseSchema"];
        if (string.IsNullOrEmpty(databaseSchema))
            throw new Exception("Missing 'DatabaseSchema' in app configuration");
    }

    public void Configure(IApplicationBuilder app) {
        app.UseRouting();

        app.UseEndpoints(endpoints => {
            var builders = new List<GrpcServiceEndpointConventionBuilder>();
            builders.AddRange(ReportingHostConfig.ConfigureEndpoints(endpoints));
        });
    }
}
