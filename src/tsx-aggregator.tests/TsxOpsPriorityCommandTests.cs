using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;
using tsx_aggregator.Services;
using tsx_ops;
using tsx_ops.Commands;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// tsx-ops priority: the RawCollector's priority queue from the deploy laptop's command line, since the site's
/// System Controls page cannot change it while the production web API is read-only.
/// </summary>
public class TsxOpsPriorityCommandTests {
    private readonly Mock<StockDataService.StockDataServiceClient> _client = new();
    private readonly StringWriter _output = new();
    private readonly List<string> _queue = [];
    private Uri? _requestedAddress;

    public TsxOpsPriorityCommandTests() {
        _ = _client.Setup(c => c.GetPriorityCompaniesAsync(It.IsAny<GetPriorityCompaniesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => {
                var reply = new GetPriorityCompaniesReply { Success = true };
                reply.CompanySymbols.Add(_queue);
                return Call(reply);
            });
        _ = _client.Setup(c => c.SetPriorityCompaniesAsync(It.IsAny<SetPriorityCompaniesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((SetPriorityCompaniesRequest r, Metadata _, DateTime? _, CancellationToken _) => {
                _queue.Clear();
                _queue.AddRange(r.CompanySymbols);
                return Call(new StockDataServiceReply { Success = true });
            });
    }

    private static AsyncUnaryCall<T> Call<T>(T reply) =>
        new(Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    private Task<int> Run(params string[] args) {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ops:AggregatorGrpcUrl"] = "http://ops.test:7001" })
            .Build();
        using OpsContext context = OpsContext.CreateForTests(config, _output);
        var command = new PriorityCommand(address => {
            _requestedAddress = address;
            return _client.Object;
        });
        return command.RunAsync(args, context, CancellationToken.None);
    }

    private void VerifyNoChange() =>
        _client.Verify(c => c.SetPriorityCompaniesAsync(It.IsAny<SetPriorityCompaniesRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Show_ListsTheQueueInOrder() {
        _queue.AddRange(["PHX", "TAL"]);

        int exit = await Run("show");

        exit.Should().Be(ExitCodes.Ok);
        _requestedAddress.Should().Be(new Uri("http://ops.test:7001"));
        _output.ToString().Should().Contain("2 in the priority queue: PHX, TAL");
    }

    [Fact]
    public async Task Show_EmptyQueue_SaysSo() {
        (await Run("show")).Should().Be(ExitCodes.Ok);

        _output.ToString().Should().Contain("The priority queue is empty.");
    }

    [Fact]
    public async Task Set_WithoutExecute_IsADryRun() {
        _queue.Add("OLD");

        int exit = await Run("set", "RY,TD");

        exit.Should().Be(ExitCodes.Ok);
        _output.ToString().Should().Contain("Dry run: would replace the priority queue OLD with RY, TD");
        VerifyNoChange();
    }

    [Fact]
    public async Task Set_WithExecute_SendsCleanedSymbolsAndShowsTheResult() {
        int exit = await Run("set", "phx, tal", "STGO", "TAL", "--execute");

        exit.Should().Be(ExitCodes.Ok);
        _queue.Should().Equal("PHX", "TAL", "STGO");
        _output.ToString().Should().Contain("The priority queue is now: PHX, TAL, STGO");
    }

    [Fact]
    public async Task Clear_WithExecute_EmptiesTheQueue() {
        _queue.AddRange(["PHX", "TAL"]);

        int exit = await Run("clear", "--execute");

        exit.Should().Be(ExitCodes.Ok);
        _queue.Should().BeEmpty();
        _output.ToString().Should().Contain("The priority queue is now empty.");
    }

    [Fact]
    public async Task Clear_WithoutExecute_IsADryRun() {
        _queue.Add("PHX");

        (await Run("clear")).Should().Be(ExitCodes.Ok);

        _output.ToString().Should().Contain("Dry run: would replace the priority queue PHX with (empty)");
        VerifyNoChange();
    }

    [Fact]
    public async Task Set_RefusedByTheAggregator_IsAnError() {
        _ = _client.Setup(c => c.SetPriorityCompaniesAsync(It.IsAny<SetPriorityCompaniesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Call(new StockDataServiceReply { Success = false, ErrorMessage = "Failed to post request" }));

        int exit = await Run("set", "RY", "--execute");

        exit.Should().Be(ExitCodes.Error);
        _output.ToString().Should().Contain("Error: Failed to post request");
    }

    [Fact]
    public async Task AggregatorNotRunning_IsAnErrorNotACrash() {
        _ = _client.Setup(c => c.GetPriorityCompaniesAsync(It.IsAny<GetPriorityCompaniesRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.Unavailable, "connection refused")));

        (await Run("show")).Should().Be(ExitCodes.Error);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("show", "RY")]
    [InlineData("clear", "RY")]
    [InlineData("bogus")]
    public async Task BadArguments_AreUsageErrors(params string[] args) {
        Func<Task> run = () => Run(args);

        await run.Should().ThrowAsync<UsageException>();
    }

    [Fact]
    public void ParseSymbols_SplitsUpperCasesAndDropsDuplicates() {
        PriorityCommand.ParseSymbols(["ry, td", "RY", " bns ,"]).Should().Equal("RY", "TD", "BNS");
    }
}
