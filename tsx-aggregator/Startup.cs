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

        // project-specific configurations
    }

    public void Configure(IApplicationBuilder app) {
        app.UseRouting();

        app.UseEndpoints(endpoints => {
            var builders = new List<GrpcServiceEndpointConventionBuilder>();
            builders.AddRange(ReportingHostConfig.ConfigureEndpoints(endpoints));
        });
    }
}
