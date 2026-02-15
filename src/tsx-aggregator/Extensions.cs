using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace tsx_aggregator;

public static class Extensions {
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName) where TOptions : class, new() {

        return services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Services;
    }
}