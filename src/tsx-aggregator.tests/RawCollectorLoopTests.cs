using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The RawCollector's main loop must survive a request that throws (code review P1): it used to log
/// "Fatal error" and stop scraping for good while the host, and tsx-ops service status, said it was running.
/// </summary>
public sealed class RawCollectorLoopTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private RawCollector? _collector;

    public RawCollectorLoopTests() {
        // Saved state with both fetches a day away, so no timeout scrapes during the test.
        DateTime farAway = DateTime.UtcNow.AddDays(1);
        _ = _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, new ApplicationCommonState(false, farAway, farAway, null, InstrumentKey.Empty)));
        _ = _dbm.Setup(d => d.GetCommonServiceState("RawCollector", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, false));
        _ = _dbm.Setup(d => d.PersistStateFsmState(It.IsAny<ApplicationCommonState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.SUCCESS);
        _ = _dbm.Setup(d => d.PersistCommonServiceState(It.IsAny<bool>(), "RawCollector", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.SUCCESS);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_collector is not null) {
            await _collector.StopAsync(CancellationToken.None);
            _collector.Dispose();
        }
    }

    private async Task<RawCollector> StartCollector() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<RawCollector>>(new NullLogger<RawCollector>());
        _ = services.AddSingleton<ILogger<RawCollectorFsm>>(new NullLogger<RawCollectorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        _ = services.AddSingleton(new Registry());
        _ = services.AddSingleton(new Mock<IHttpClientFactory>().Object);
        _collector = new RawCollector(services.BuildServiceProvider()) { ErrorBackoff = TimeSpan.Zero };
        await _collector.StartAsync(CancellationToken.None);
        return _collector;
    }

    [Fact]
    public async Task RequestThatThrows_FailsItsCallerAndTheLoopKeepsRunning() {
        _ = _dbm.Setup(d => d.GetInstrumentsWithNoRawReports(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db exploded"));
        RawCollector collector = await StartCollector();

        using var failing = new RawCollectorGetInstrumentsWithNoRawReportsInput(1, Constants.TsxExchange, 1, 10, null);
        collector.PostRequest(failing).Should().BeTrue();

        // The caller hears about the failure instead of waiting forever.
        Func<Task> wait = () => failing.Completed.Task.WaitAsync(Deadline);
        _ = await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("db exploded");

        // And the loop is still alive.
        await collector.SetPausedAsync(true, CancellationToken.None).WaitAsync(Deadline);
        collector.IsPaused.Should().BeTrue();
    }
}
