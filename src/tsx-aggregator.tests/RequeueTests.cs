using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using tsx_ops;
using tsx_ops.Commands;

namespace tsx_aggregator.tests;

/// <summary>O1 (decision 26): tsx-ops requeue through AdminService.</summary>
public class RequeueTests {
    private static readonly RequeueCandidateDto Abc = new(1, "ABC", HasRawReports: true, HasPendingRawDataChanged: false);
    private static readonly RequeueCandidateDto Def = new(2, "DEF", HasRawReports: true, HasPendingRawDataChanged: true);
    private static readonly RequeueCandidateDto Etf = new(3, "ETF", HasRawReports: false, HasPendingRawDataChanged: false);
    private static readonly RequeueCandidateDto Xyz = new(4, "XYZ", HasRawReports: true, HasPendingRawDataChanged: false);
    private static readonly RequeueCandidateDto[] Candidates = [Xyz, Abc, Def, Etf];

    #region Planner

    [Fact]
    public void Plan_All_QueuesInstrumentsWithRawDataNotAlreadyWaiting() {
        RequeuePlan plan = RequeuePlanner.Plan(Candidates, symbols: null);

        _ = plan.ToQueue.Select(c => c.InstrumentSymbol).Should().Equal("ABC", "XYZ");
        _ = plan.AlreadyPending.Should().Equal("DEF");
        _ = plan.NoRawData.Should().Equal("ETF");
        _ = plan.Unknown.Should().BeEmpty();
    }

    [Fact]
    public void Plan_Symbols_MatchCaseInsensitivelyAndReportUnknown() {
        RequeuePlan plan = RequeuePlanner.Plan(Candidates, ["abc", "ABC", "NOPE"]);

        _ = plan.ToQueue.Select(c => c.InstrumentId).Should().Equal(1);
        _ = plan.Unknown.Should().Equal("NOPE");
    }

    #endregion

    #region AdminService

    private sealed class Waker : IEventQueueWaker {
        public int Calls { get; private set; }
        public void CheckForEventsNow() => Calls++;
    }

    private sealed class TestServerCallContext : ServerCallContext {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, []);
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private readonly Waker _waker = new();
    private List<long>? _inserted;

    private AdminSvc CreateService(bool aggregatorRuns = true) {
        _ = _dbm.Setup(d => d.GetRequeueCandidates(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (IReadOnlyList<RequeueCandidateDto>)Candidates));
        _ = _dbm.Setup(d => d.InsertRawDataChangedEvents(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<long> ids, CancellationToken _) => {
                _inserted = [.. ids];
                return Result.SUCCESS;
            });
        return new AdminSvc([], Options.Create(new HostedServicesOptions { RunAggregator = aggregatorRuns }),
            _dbm.Object, NullLogger<AdminSvc>.Instance, _waker);
    }

    [Fact]
    public async Task Requeue_DryRun_ReportsWithoutInserting() {
        RequeueInstrumentsReply reply = await CreateService().RequeueInstruments(
            new RequeueInstrumentsRequest { All = true }, new TestServerCallContext());

        _ = reply.Success.Should().BeTrue();
        _ = reply.Executed.Should().BeFalse();
        _ = reply.QueuedSymbols.Should().Equal("ABC", "XYZ");
        _ = _inserted.Should().BeNull();
        _ = _waker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Requeue_Execute_InsertsAndWakesTheAggregator() {
        RequeueInstrumentsReply reply = await CreateService().RequeueInstruments(
            new RequeueInstrumentsRequest { All = true, Execute = true }, new TestServerCallContext());

        _ = reply.Executed.Should().BeTrue();
        _ = _inserted.Should().BeEquivalentTo([1L, 4L]);
        _ = reply.AggregatorWoken.Should().BeTrue();
        _ = _waker.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Requeue_Execute_AggregatorNotRunning_InsertsWithoutWaking() {
        RequeueInstrumentsReply reply = await CreateService(aggregatorRuns: false).RequeueInstruments(
            new RequeueInstrumentsRequest { All = true, Execute = true }, new TestServerCallContext());

        _ = reply.Executed.Should().BeTrue();
        _ = reply.AggregatorWoken.Should().BeFalse();
        _ = _waker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Requeue_UnknownSymbol_QueuesNothing() {
        var request = new RequeueInstrumentsRequest { Execute = true };
        request.InstrumentSymbols.AddRange(["ABC", "TYPO"]);

        RequeueInstrumentsReply reply = await CreateService().RequeueInstruments(request, new TestServerCallContext());

        _ = reply.Success.Should().BeFalse();
        _ = reply.UnknownSymbols.Should().Equal("TYPO");
        _ = _inserted.Should().BeNull();
    }

    #endregion

    #region tsx-ops command

    private readonly Mock<AdminService.AdminServiceClient> _client = new();
    private readonly StringWriter _output = new();
    private RequeueInstrumentsRequest? _sent;

    private static AsyncUnaryCall<T> Call<T>(T reply) =>
        new(Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    private Task<int> RunCommand(RequeueInstrumentsReply reply, params string[] args) {
        _ = _client.Setup(c => c.RequeueInstrumentsAsync(It.IsAny<RequeueInstrumentsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback((RequeueInstrumentsRequest r, Metadata _, DateTime? _, CancellationToken _) => _sent = r)
            .Returns(Call(reply));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ops:AggregatorGrpcUrl"] = "http://ops.test:7001" })
            .Build();
        using OpsContext context = OpsContext.CreateForTests(config, _output);
        return new RequeueCommand(_ => _client.Object).RunAsync(args, context, CancellationToken.None);
    }

    [Fact]
    public async Task Command_WithoutExecute_SendsADryRun() {
        var reply = new RequeueInstrumentsReply { Success = true };
        reply.QueuedSymbols.AddRange(["ABC", "XYZ"]);

        int exit = await RunCommand(reply, "--all");

        _ = exit.Should().Be(ExitCodes.Ok);
        _ = _sent!.Execute.Should().BeFalse();
        _ = _sent.All.Should().BeTrue();
        _ = _output.ToString().Should().Contain("Would queue 2: ABC XYZ").And.Contain("--execute");
    }

    [Fact]
    public async Task Command_Symbols_AreSentAndAnErrorFails() {
        var reply = new RequeueInstrumentsReply { Success = false, ErrorMessage = "Not listed instruments: TYPO. Nothing queued." };
        reply.UnknownSymbols.Add("TYPO");

        int exit = await RunCommand(reply, "abc", "TYPO", "--execute");

        _ = exit.Should().Be(ExitCodes.Error);
        _ = _sent!.InstrumentSymbols.Should().Equal("abc", "TYPO");
        _ = _sent.Execute.Should().BeTrue();
    }

    [Fact]
    public async Task Command_NeitherAllNorSymbols_IsAUsageError() {
        Func<Task> act = () => RunCommand(new RequeueInstrumentsReply(), "--execute");

        _ = await act.Should().ThrowAsync<UsageException>();
    }

    #endregion
}
