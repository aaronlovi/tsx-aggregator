using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using tsx_aggregator.Services;

namespace tsx_aggregator;

// This helper class is used by the main startup config. This allows to keep registered classes as 'internal'.
public static class ReportingHostConfig {
    public static void ConfigureServices(IServiceCollection _) { }

    /// <summary>
    /// Used at application startup to configure gRPC endpoints.
    /// In this case, incoming HTTP requests are routed to gRPC endpoints
    /// in the <see cref="StockDataService"/> class.
    /// </summary>
    public static IEnumerable<GrpcServiceEndpointConventionBuilder> ConfigureEndpoints(IEndpointRouteBuilder endpoints)
        => new[] { endpoints.MapGrpcService<StockDataSvc>() };
}
