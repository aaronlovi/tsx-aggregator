using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.Aggregated;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>Each long-running service logs a heartbeat as soon as it starts (and once a minute after that).</summary>
public sealed class HeartbeatTests {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>Keeps every formatted message, so a test can wait for one.</summary>
    private sealed class RecordingLogger<T> : ILogger<T> {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }

    private static async Task ExpectHeartbeat<T>(BackgroundService service, RecordingLogger<T> logger, Func<string, bool> isHeartbeat) {
        await service.StartAsync(CancellationToken.None);
        try {
            using var timeout = new CancellationTokenSource(Deadline);
            while (!logger.Messages.Any(isHeartbeat))
                await Task.Delay(10, timeout.Token);
        } finally {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    [Fact]
    public async Task Aggregator_LogsAHeartbeat() {
        var logger = new RecordingLogger<Aggregator>();
        var dbm = new Mock<IDbmService>();
        _ = dbm.Setup(d => d.GetCommonServiceState("Aggregator", It.IsAny<CancellationToken>())).ReturnsAsync((Result.SUCCESS, false));
        _ = dbm.Setup(d => d.GetNextInstrumentEvent(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (InstrumentEventExDto?)null));
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<Aggregator>>(logger);
        _ = services.AddSingleton<ILogger<AggregatorFsm>>(new NullLogger<AggregatorFsm>());
        _ = services.AddSingleton(dbm.Object);

        await ExpectHeartbeat(new Aggregator(services.BuildServiceProvider()), logger, m => m == "Aggregator heartbeat");
    }

    [Fact]
    public async Task StocksDataRequestsProcessor_LogsAHeartbeat() {
        var logger = new RecordingLogger<StocksDataRequestsProcessor>();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<StocksDataRequestsProcessor>>(logger);
        _ = services.AddSingleton(new Mock<IDbmService>().Object);

        await ExpectHeartbeat(new StocksDataRequestsProcessor(services.BuildServiceProvider()), logger,
            m => m == "StocksDataFetchingService heartbeat");
    }

    [Fact]
    public async Task SearchService_LogsAHeartbeat() {
        var logger = new RecordingLogger<SearchService>();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<SearchService>>(logger);
        _ = services.AddSingleton(new Registry());

        await ExpectHeartbeat(new SearchService(services.BuildServiceProvider()), logger, m => m == "SearchService heartbeat");
    }

    [Fact]
    public async Task QuoteService_LogsAHeartbeatWithTheNextFetchTime() {
        var logger = new RecordingLogger<QuoteService>();
        QuoteService? service = null;
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<QuoteService>>(logger);
        _ = services.AddSingleton(new Registry());
        _ = services.AddSingleton(new Mock<IDbmService>().Object);
        _ = services.AddSingleton(new Mock<IGoogleSheetsService>().Object);
        _ = services.AddSingleton<IQuoteService>(_ => service!);
        service = new QuoteService(services.BuildServiceProvider());

        await ExpectHeartbeat(service, logger,
            m => m == "QuoteService heartbeat - Next quotes fetch time not known (saved state not loaded)");
    }
}
