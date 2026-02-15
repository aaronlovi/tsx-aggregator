using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using tsx_aggregator.models;

namespace tsx_aggregator;

public class Startup {
    private readonly IConfiguration _config;

    /// <summary>
    /// 1. Application Initialization
    /// When the application starts, the Startup class is instantiated and configured.
    /// </summary>
    /// <param name="config"></param>
    public Startup(IConfiguration config) => _config = config;

    /// <summary>
    /// 2. Service Configuration
    /// The ConfigureServices method is called by the runtime.
    /// This method is used to add services to the DI container.
    /// These services are then available throughout the application
    /// </summary>
    /// <param name="services"></param>
    public void ConfigureServices(IServiceCollection services) {
        _ = services.AddGrpc();
        _ = services.Configure<GoogleCredentialsOptions>(_config.GetSection(GoogleCredentialsOptions.GoogleCredentials));
        _ = services.Configure<HostedServicesOptions>(_config.GetSection(HostedServicesOptions.HostedServices));

        VerifyCriticalConfiguration();
    }

    /// <summary>
    /// 3. Request Pipeline Configuration
    /// The Configure method is called by the runtime to set up the request processing pipeline.
    /// This method defines how the application will respond to HTTP requests.
    /// </summary>
    public static void Configure(IApplicationBuilder app) {
        _ = app.
            UseRouting().
            UseEndpoints(endpoints => {
                // In this case, incoming HTTP requests are routed to gRPC endpoints,
                // which are configured in ReportingHostConfig.ConfigureEndpoints
                var builders = new List<GrpcServiceEndpointConventionBuilder>();
                builders.AddRange(ReportingHostConfig.ConfigureEndpoints(endpoints));
            });
    }

    #region PRIVATE HELPER METHODS

    private void VerifyCriticalConfiguration() {
        VerifyConfigurationItem("DatabaseSchema");

        VerifyConfigurationSection(GoogleCredentialsOptions.GoogleCredentials);
        VerifyConfigurationSection(HostedServicesOptions.HostedServices);
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

    #endregion
}
