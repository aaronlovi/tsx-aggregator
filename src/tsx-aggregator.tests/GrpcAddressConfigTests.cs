using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using stock_market_webapi;

namespace tsx_aggregator.tests;

/// <summary>
/// D1: the web API's address for tsx-aggregator comes from settings.
/// D2: tsx-aggregator's gRPC port listens on localhost unless told otherwise.
/// </summary>
public class GrpcAddressConfigTests {
    private static IConfiguration Config(params (string Key, string Value)[] values) {
        var dict = new Dictionary<string, string?>();
        foreach ((string key, string value) in values)
            dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void WebApi_NoSetting_UsesLocalhost7001() {
        _ = AggregatorClientSettings.GetGrpcUrl(Config()).Should().Be(new Uri("http://localhost:7001"));
    }

    [Fact]
    public void WebApi_Setting_UsesIt() {
        Uri url = AggregatorClientSettings.GetGrpcUrl(Config(("Aggregator:GrpcUrl", "http://localhost:7002")));

        _ = url.Should().Be(new Uri("http://localhost:7002"));
    }

    [Fact]
    public void WebApi_EnvironmentVariableName_MapsToTheSetting() {
        // run.sh and Windows services set it as Aggregator__GrpcUrl.
        Environment.SetEnvironmentVariable("TSXTEST_Aggregator__GrpcUrl", "http://localhost:7003");
        try {
            IConfiguration cfg = new ConfigurationBuilder().AddEnvironmentVariables("TSXTEST_").Build();

            _ = AggregatorClientSettings.GetGrpcUrl(cfg).Should().Be(new Uri("http://localhost:7003"));
        } finally {
            Environment.SetEnvironmentVariable("TSXTEST_Aggregator__GrpcUrl", null);
        }
    }

    [Theory]
    [InlineData("localhost:7001")]
    [InlineData("not a url")]
    [InlineData("ftp://localhost:7001")]
    public void WebApi_BadSetting_FailsWithTheKeyName(string value) {
        Action act = () => AggregatorClientSettings.GetGrpcUrl(Config(("Aggregator:GrpcUrl", value)));

        _ = act.Should().Throw<InvalidOperationException>().WithMessage("*Aggregator:GrpcUrl*");
    }

    [Fact]
    public void Aggregator_NoSettings_Localhost7001() {
        _ = Program.GetGrpcListener(Config()).Should().Be(new GrpcListener(7001, AllInterfaces: false));
    }

    [Fact]
    public void Aggregator_PortSetting_LocalhostOnThatPort() {
        _ = Program.GetGrpcListener(Config(("Ports:Grpc", "7002"))).Should().Be(new GrpcListener(7002, AllInterfaces: false));
    }

    [Fact]
    public void Aggregator_AllInterfacesSetting_ListensOnAll() {
        GrpcListener listener = Program.GetGrpcListener(Config(("Ports:GrpcListenOnAllInterfaces", "true")));

        _ = listener.Should().Be(new GrpcListener(7001, AllInterfaces: true));
    }

    [Theory]
    [InlineData("Ports:Grpc", "abc")]
    [InlineData("Ports:Grpc", "0")]
    [InlineData("Ports:Grpc", "70000")]
    [InlineData("Ports:GrpcListenOnAllInterfaces", "yes")]
    public void Aggregator_BadSetting_FailsWithTheKeyName(string key, string value) {
        Action act = () => Program.GetGrpcListener(Config((key, value)));

        _ = act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }
}
