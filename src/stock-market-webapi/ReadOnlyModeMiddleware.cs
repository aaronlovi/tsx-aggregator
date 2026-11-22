using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace stock_market_webapi;

/// <summary>Web API settings outside the aggregator address.</summary>
public static class ApiSettings {
    /// <summary>Settings key; as an environment variable, <c>Api__ReadOnly</c>.</summary>
    public const string ReadOnlyKey = "Api:ReadOnly";

    /// <summary>True when the API must refuse writes (the deployed site). Throws on a value that is not a boolean.</summary>
    public static bool IsReadOnly(IConfiguration configuration) {
        string? value = configuration[ReadOnlyKey];
        if (value is null)
            return false;
        if (!bool.TryParse(value, out bool readOnly))
            throw new InvalidOperationException($"{ReadOnlyKey} must be true or false, got '{value}'");
        return readOnly;
    }
}

/// <summary>
/// Refuses every request except GET, HEAD and OPTIONS (CORS preflight) with 405 Method Not Allowed.
/// Used when <see cref="ApiSettings.ReadOnlyKey"/> is true, so nobody who can reach the site can change anything.
/// </summary>
public sealed class ReadOnlyModeMiddleware {
    private const string AllowedMethods = "GET, HEAD, OPTIONS";
    private readonly RequestDelegate _next;

    public ReadOnlyModeMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context) {
        string method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return _next(context);

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = AllowedMethods;
        return Task.CompletedTask;
    }
}
