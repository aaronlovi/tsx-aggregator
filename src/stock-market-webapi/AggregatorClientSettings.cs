using System;
using Microsoft.Extensions.Configuration;

namespace stock_market_webapi;

/// <summary>Where the web API finds tsx-aggregator's gRPC service.</summary>
public static class AggregatorClientSettings {
    /// <summary>Settings key; as an environment variable, <c>Aggregator__GrpcUrl</c>.</summary>
    public const string GrpcUrlKey = "Aggregator:GrpcUrl";
    public const string DefaultGrpcUrl = "http://localhost:7001";

    /// <summary>The configured address, or the default. Throws on a value that is not an absolute http(s) URL.</summary>
    public static Uri GetGrpcUrl(IConfiguration configuration) {
        string value = configuration[GrpcUrlKey] ?? DefaultGrpcUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"{GrpcUrlKey} must be an absolute http or https URL such as {DefaultGrpcUrl}, got '{value}'");
        return url;
    }
}
