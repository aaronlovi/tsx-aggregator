using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;
using tsx_aggregator.Services;
using tsx_ops;
using tsx_ops.Commands;

namespace tsx_aggregator.tests;

public class TsxOpsServiceCommandTests {
    private readonly Mock<AdminService.AdminServiceClient> _client = new();
    private readonly StringWriter _output = new();
    private Uri? _requestedAddress;

    private static AsyncUnaryCall<T> Call<T>(T reply) =>
        new(Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    private void SetupStatus(params (string Name, bool Running, bool Paused)[] services) {
        var reply = new GetServiceStatusReply { Success = true };
        foreach ((string name, bool running, bool paused) in services)
            reply.Services.Add(new ServiceStatus { ServiceName = name, IsRunning = running, IsPaused = paused });
        _ = _client.Setup(c => c.GetServiceStatusAsync(It.IsAny<GetServiceStatusRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(reply));
    }

    private Task<int> Run(params string[] args) {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ops:AggregatorGrpcUrl"] = "http://ops.test:7001" })
            .Build();
        using OpsContext context = OpsContext.CreateForTests(config, _output);
        var command = new ServiceCommand(address => {
            _requestedAddress = address;
            return _client.Object;
        });
        return command.RunAsync(args, context, CancellationToken.None);
    }

    [Fact]
    public async Task Status_PrintsEachService() {
        SetupStatus(("Aggregator", true, false), ("RawCollector", false, true));

        int exit = await Run("status");

        _ = exit.Should().Be(ExitCodes.Ok);
        _ = _requestedAddress.Should().Be(new Uri("http://ops.test:7001"));
        string[] lines = _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _ = lines.Should().HaveCount(3);
        _ = lines[1].Should().MatchRegex(@"^Aggregator\s+Yes\s+No\s*$");
        _ = lines[2].Should().MatchRegex(@"^RawCollector\s+No\s+Yes\s*$");
    }

    [Fact]
    public async Task Pause_WithoutExecute_IsADryRun() {
        SetupStatus(("RawCollector", true, false));

        int exit = await Run("pause", "RawCollector");

        _ = exit.Should().Be(ExitCodes.Ok);
        _ = _output.ToString().Should().Contain("Dry run: would pause RawCollector");
        _client.Verify(c => c.SetServicePausedAsync(It.IsAny<SetServicePausedRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pause_WithExecute_CallsTheService() {
        SetupStatus(("RawCollector", false, false));
        _ = _client.Setup(c => c.SetServicePausedAsync(
                It.Is<SetServicePausedRequest>(r => r.ServiceName == "RawCollector" && r.Paused), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(new SetServicePausedReply { Success = true, WasPaused = false, IsPaused = true, AppliedToRunningService = false }));

        int exit = await Run("pause", "rawcollector", "--execute");

        _ = exit.Should().Be(ExitCodes.Ok);
        _ = _output.ToString().Should().Contain("RawCollector: paused no -> yes (saved; applies when it next starts)");
    }

    [Fact]
    public async Task Resume_AlreadyRunning_DoesNothing() {
        SetupStatus(("Aggregator", true, false));

        int exit = await Run("resume", "Aggregator", "--execute");

        _ = exit.Should().Be(ExitCodes.Ok);
        _ = _output.ToString().Should().Contain("already not paused");
        _client.Verify(c => c.SetServicePausedAsync(It.IsAny<SetServicePausedRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pause")]
    [InlineData("bogus")]
    [InlineData("status extra")]
    public async Task BadArguments_ThrowUsageException(string spaceSeparatedArgs) {
        SetupStatus(("Aggregator", true, false));
        string[] args = spaceSeparatedArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Func<Task> act = () => Run(args);

        _ = await act.Should().ThrowAsync<UsageException>();
    }

    [Fact]
    public async Task UnknownService_ThrowsUsageExceptionListingKnownServices() {
        SetupStatus(("Aggregator", true, false), ("RawCollector", true, false));

        Func<Task> act = () => Run("pause", "Nope");

        _ = (await act.Should().ThrowAsync<UsageException>()).Which.Message.Should().Contain("Aggregator, RawCollector");
    }

    [Fact]
    public async Task AggregatorUnreachable_ReturnsError() {
        _ = _client.Setup(c => c.GetServiceStatusAsync(It.IsAny<GetServiceStatusRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "connection refused")));

        int exit = await Run("status");

        _ = exit.Should().Be(ExitCodes.Error);
    }
}
