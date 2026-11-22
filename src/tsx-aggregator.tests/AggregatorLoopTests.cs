using System;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.Aggregated;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The Aggregator's main loop must survive an abandoned request (code review P13): a request whose poster
/// had given up used to throw ObjectDisposedException in the loop, which ended it for good while the host kept running.
/// </summary>
public sealed class AggregatorLoopTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private readonly TaskCompletionSource _releaseEventCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _eventCheckStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Aggregator? _aggregator;

    public AggregatorLoopTests() {
        _ = _dbm.Setup(d => d.GetCommonServiceState("Aggregator", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, false));
        // The first event check blocks until the test releases it, so the test controls when the loop reads its next input.
        _ = _dbm.Setup(d => d.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .Returns(async () => {
                _ = _eventCheckStarted.TrySetResult();
                await _releaseEventCheck.Task;
                return (Result.SUCCESS, (InstrumentEventExDto?)null);
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        _releaseEventCheck.TrySetResult();
        if (_aggregator is not null) {
            await _aggregator.StopAsync(CancellationToken.None);
            _aggregator.Dispose();
        }
    }

    private async Task<Aggregator> StartAggregator() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<Aggregator>>(new NullLogger<Aggregator>());
        _ = services.AddSingleton<ILogger<AggregatorFsm>>(new NullLogger<AggregatorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        _aggregator = new Aggregator(services.BuildServiceProvider());
        await _aggregator.StartAsync(CancellationToken.None);
        await _eventCheckStarted.Task.WaitAsync(Deadline);
        return _aggregator;
    }

    [Fact]
    public async Task PauseRequest_AbandonedByItsCaller_DoesNotStopTheLoop() {
        _ = _dbm.Setup(d => d.PersistCommonServiceState(true, "Aggregator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.SUCCESS);
        Aggregator aggregator = await StartAggregator();

        // The caller gives up at once, so its request object (and the token source it owns) is disposed
        // while the request is still queued behind the blocked event check.
        Func<Task> abandoned = () => aggregator.SetPausedAsync(true, new CancellationToken(canceled: true));
        _ = await abandoned.Should().ThrowAsync<OperationCanceledException>();
        _releaseEventCheck.SetResult();

        // A later request must still be served.
        await aggregator.SetPausedAsync(true, CancellationToken.None).WaitAsync(Deadline);
        aggregator.IsPaused.Should().BeTrue();
    }
}
