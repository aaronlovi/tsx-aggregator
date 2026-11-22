using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using stock_market_webapi;

namespace tsx_aggregator.tests;

/// <summary>
/// D5: with Api:ReadOnly the web API refuses anything but GET, HEAD and OPTIONS with a 405, so nobody on the
/// network can change anything (POST /companies/priority is the one write route).
/// </summary>
public class ReadOnlyModeTests {
    private static async Task<(int Status, bool NextCalled, string Allow)> Send(string method) {
        bool nextCalled = false;
        var middleware = new ReadOnlyModeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/companies/priority";

        await middleware.InvokeAsync(context);

        return (context.Response.StatusCode, nextCalled, context.Response.Headers.Allow.ToString());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task ReadMethods_PassThrough(string method) {
        (int status, bool nextCalled, _) = await Send(method);

        _ = nextCalled.Should().BeTrue();
        _ = status.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("post")]
    public async Task WriteMethods_Get405(string method) {
        (int status, bool nextCalled, string allow) = await Send(method);

        _ = nextCalled.Should().BeFalse();
        _ = status.Should().Be(StatusCodes.Status405MethodNotAllowed);
        _ = allow.Should().Be("GET, HEAD, OPTIONS");
    }

    private static IConfiguration Config(string? value) {
        var values = new Dictionary<string, string?>();
        if (value is not null)
            values["Api:ReadOnly"] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Setting_DefaultsToOff() {
        _ = ApiSettings.IsReadOnly(Config(null)).Should().BeFalse();
    }

    [Fact]
    public void Setting_TrueTurnsItOn() {
        _ = ApiSettings.IsReadOnly(Config("true")).Should().BeTrue();
    }

    [Fact]
    public void Setting_NotABoolean_FailsWithTheKeyName() {
        Action act = () => ApiSettings.IsReadOnly(Config("yes"));

        _ = act.Should().Throw<InvalidOperationException>().WithMessage("*Api:ReadOnly*");
    }
}
