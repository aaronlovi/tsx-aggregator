using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using tsx_aggregator.models;

namespace tsx_aggregator;

public class Startup {
    private readonly IConfiguration _config;

    public Startup(IConfiguration config) => _config = config;

    public void ConfigureServices(IServiceCollection services) {
        services.AddGrpc();
        services.Configure<GoogleCredentialsOptions>(_config.GetSection("GoogleCredentials"));

        VerifyCriticalConfiguration();
    }

    private void VerifyCriticalConfiguration() {
        VerifyConfigurationItem("tsx-scraper", "ConnectionStrings");
        VerifyConfigurationItem("DatabaseSchema");
        VerifyConfigurationSection("GoogleCredentials");
    }

    private void VerifyConfigurationItem(string key, string? section = null) {
        string? value;
        if (section is not null) {
            value = _config.GetSection(section)[key];
            if (string.IsNullOrEmpty(value)) 
                throw new Exception($"Missing '{key}' in '{section}' section of app configuration");
        } else {
            value = _config[key];
            if (string.IsNullOrEmpty(value))
                throw new Exception($"Missing '{key}' in app configuration");
        }
    }

    private void VerifyConfigurationSection(string section) {
        if (_config.GetSection(section) is null)
            throw new Exception($"Missing '{section}' section in app configuration");
    }

    public static void Configure(IApplicationBuilder app) {
        app.UseRouting();

        app.UseEndpoints(endpoints => {
            var builders = new List<GrpcServiceEndpointConventionBuilder>();
            builders.AddRange(ReportingHostConfig.ConfigureEndpoints(endpoints));
        });
    }
}
