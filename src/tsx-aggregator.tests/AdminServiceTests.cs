using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.Aggregated;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

public class AdminServiceTests {
    private sealed class FakePausableService(string name, bool paused) : IPausableService {
        public string ServiceName { get; } = name;
        public bool IsPaused { get; private set; } = paused;
        public int Calls { get; private set; }

        public Task SetPausedAsync(bool p, CancellationToken ct) {
            Calls++;
            IsPaused = p;
            return Task.CompletedTask;
        }
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
    private readonly Dictionary<string, bool> _savedState = new();

    public AdminServiceTests() {
        _ = _dbm.Setup(d => d.GetCommonServiceState(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => (Result.SUCCESS, _savedState.GetValueOrDefault(name)));
        _ = _dbm.Setup(d => d.PersistCommonServiceState(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool paused, string name, CancellationToken _) => {
                _savedState[name] = paused;
                return Result.SUCCESS;
            });
    }

    private AdminSvc CreateService(bool aggregatorRuns, bool rawCollectorRuns, params IPausableService[] services) =>
        new(services,
            Options.Create(new HostedServicesOptions { RunAggregator = aggregatorRuns, RunRawCollector = rawCollectorRuns }),
            _dbm.Object,
            NullLogger<AdminSvc>.Instance);

    [Fact]
    public async Task GetServiceStatus_ReportsLiveStateForRunningAndSavedStateForStopped() {
        _savedState["RawCollector"] = true;
        var aggregator = new FakePausableService("Aggregator", paused: false);
        var rawCollector = new FakePausableService("RawCollector", paused: false);
        AdminSvc svc = CreateService(aggregatorRuns: true, rawCollectorRuns: false, aggregator, rawCollector);

        GetServiceStatusReply reply = await svc.GetServiceStatus(new GetServiceStatusRequest(), new TestServerCallContext());

        _ = reply.Success.Should().BeTrue();
        _ = reply.Services.Select(s => (s.ServiceName, s.IsRunning, s.IsPaused))
            .Should().Equal(("Aggregator", true, false), ("RawCollector", false, true));
    }

    [Fact]
    public async Task SetServicePaused_RunningService_GoesThroughTheService() {
        var aggregator = new FakePausableService("Aggregator", paused: false);
        AdminSvc svc = CreateService(aggregatorRuns: true, rawCollectorRuns: false, aggregator);

        SetServicePausedReply reply = await svc.SetServicePaused(
            new SetServicePausedRequest { ServiceName = "aggregator", Paused = true }, new TestServerCallContext());

        _ = reply.Success.Should().BeTrue();
        _ = (reply.WasPaused, reply.IsPaused, reply.AppliedToRunningService).Should().Be((false, true, true));
        _ = aggregator.Calls.Should().Be(1);
        _dbm.Verify(d => d.PersistCommonServiceState(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetServicePaused_StoppedService_SavesStateForNextStart() {
        var rawCollector = new FakePausableService("RawCollector", paused: false);
        AdminSvc svc = CreateService(aggregatorRuns: true, rawCollectorRuns: false, rawCollector);

        SetServicePausedReply reply = await svc.SetServicePaused(
            new SetServicePausedRequest { ServiceName = "RawCollector", Paused = true }, new TestServerCallContext());

        _ = reply.Success.Should().BeTrue();
        _ = (reply.WasPaused, reply.IsPaused, reply.AppliedToRunningService).Should().Be((false, true, false));
        _ = rawCollector.Calls.Should().Be(0);
        _ = _savedState["RawCollector"].Should().BeTrue();
    }

    [Fact]
    public async Task SetServicePaused_UnknownService_Fails() {
        AdminSvc svc = CreateService(aggregatorRuns: true, rawCollectorRuns: true, new FakePausableService("Aggregator", false));

        SetServicePausedReply reply = await svc.SetServicePaused(
            new SetServicePausedRequest { ServiceName = "Nope", Paused = true }, new TestServerCallContext());

        _ = reply.Success.Should().BeFalse();
        _ = reply.ErrorMessage.Should().Contain("Aggregator");
    }

    [Fact]
    public async Task Aggregator_RestoresSavedPauseAndCompletesNoOpAndRealChanges() {
        _savedState["Aggregator"] = true;
        _ = _dbm.Setup(d => d.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (InstrumentEventExDto?)null));
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<Aggregator>>(new NullLogger<Aggregator>());
        _ = services.AddSingleton<ILogger<AggregatorFsm>>(new NullLogger<AggregatorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        var aggregator = new Aggregator(services.BuildServiceProvider());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await aggregator.StartAsync(timeout.Token);
        try {
            await WaitUntil(() => aggregator.IsPaused, timeout.Token);

            // Already paused: no state change, but the request must still complete.
            await aggregator.SetPausedAsync(true, timeout.Token);
            _ = aggregator.IsPaused.Should().BeTrue();

            await aggregator.SetPausedAsync(false, timeout.Token);
            _ = aggregator.IsPaused.Should().BeFalse();
            _ = _savedState["Aggregator"].Should().BeFalse();
        } finally {
            await aggregator.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct) {
        while (!condition())
            await Task.Delay(20, ct);
    }
}
