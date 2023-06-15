using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using tsx_aggregator.Services;

namespace tsx_aggregator;

// This helper class is used by the main startup config. This allows to keep registered classes as 'internal'.
public static class ReportingHostConfig {
    public static void ConfigureServices(IServiceCollection services) {
    }

    public static IEnumerable<GrpcServiceEndpointConventionBuilder> ConfigureEndpoints(IEndpointRouteBuilder endpoints) {
        return new[] { endpoints.MapGrpcService<StockDataSvc>() };
    }
}
