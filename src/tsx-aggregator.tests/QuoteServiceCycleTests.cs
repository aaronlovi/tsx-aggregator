using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The QuoteService fetch cycle against a fake Google sheet (code review step 2: P3, P4, P5, M2, M6; R95, R100).
/// </summary>
public sealed class QuoteServiceCycleTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly string[] Fallback = ["C"];

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private readonly Mock<IGoogleSheetsService> _sheets = new();
    private readonly Queue<Dictionary<string, decimal>> _reads = new();
    private readonly RecordingLogger _logger = new();
    private int _readCount;
    private QuoteService? _service;

    public QuoteServiceCycleTests() {
        SetupSavedState(Result.SUCCESS, nextFetch: null); // a fetch is due at once
        _ = _dbm.Setup(d => d.UpdateNextTimeToFetchQuotes(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.SUCCESS);
        // Each ReadQuotes returns the next queued sheet, or the last one again.
        Dictionary<string, decimal> last = [];
        _ = _sheets.Setup(s => s.ReadQuotes("A", "B", Fallback, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => {
                _ = Interlocked.Increment(ref _readCount);
                if (_reads.Count > 0)
                    last = _reads.Dequeue();
                return new Dictionary<string, decimal>(last);
            });
    }

    /// <summary>What HttpClient throws when its own 100 s timeout ends a call: a cancellation nobody asked for.</summary>
    private static TaskCanceledException HttpTimeout() =>
        new("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException());

    private void SetupSavedState(Result result, DateTime? nextFetch) =>
        _dbm.Setup(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((result, result.Success ? new ApplicationCommonState(false, null, null, nextFetch, InstrumentKey.Empty) : null));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_service is not null) {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }
    }

    private async Task<QuoteService> Start(TimeSpan? maxPriceAge = null) {
        var registry = new Registry();
        registry.InitializeDirectory([]);
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<QuoteService>>(_logger);
        _ = services.AddSingleton(registry);
        _ = services.AddSingleton(_dbm.Object);
        _ = services.AddSingleton(_sheets.Object);
        _ = services.AddSingleton<IQuoteService>(p => _service!);
        _service = new QuoteService(services.BuildServiceProvider()) {
            FailureRetryBase = TimeSpan.FromMilliseconds(200),
            StateLoadRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxPriceAge = maxPriceAge ?? TimeSpan.FromDays(1),
        };
        await _service.StartAsync(CancellationToken.None);
        return _service;
    }

    private static async Task<IDictionary<string, decimal>> Prices(QuoteService service, params string[] symbols) {
        using var input = new QuoteServiceFillPricesForSymbolsInput(1, null, symbols);
        service.PostRequest(input).Should().BeTrue();
        return (IDictionary<string, decimal>)(await input.Completed.Task.WaitAsync(Deadline))!;
    }

    private static async Task Eventually(Func<bool> condition) {
        using var timeout = new CancellationTokenSource(Deadline);
        while (!condition()) {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task SheetFailingWhileAFetchIsDue_IsRetriedWithBackOff_NotInATightLoop() {
        // P3: any exception left the fetch due, and the finally block scheduled the next try with no delay.
        int clears = 0;
        _ = _sheets.Setup(s => s.ClearColumns()).Callback(() => Interlocked.Increment(ref clears)).Throws(new InvalidOperationException("quota"));
        _ = await Start();

        await Task.Delay(TimeSpan.FromSeconds(1));

        // Back-off of 200, 400, 800 ms: three or four tries in a second. A tight loop makes thousands.
        clears.Should().BeInRange(1, 6);
    }

    [Fact]
    public async Task FailedFetch_DoesNotPushTheNextFetchTwoHoursAway() {
        // M6: the next fetch time was saved before the new quotes were read, so a failed read waited two hours.
        int overrides = 0;
        _ = _sheets.SetupSequence(s => s.FetchQuoteOverrides(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sheet down"))
            .Returns(() => { _ = Interlocked.Increment(ref overrides); return Task.CompletedTask; });
        _ = await Start();

        await Eventually(() => overrides == 1); // retried after the back-off, not two hours later

        _dbm.Verify(d => d.UpdateNextTimeToFetchQuotes(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.AtMostOnce());
    }

    [Fact]
    public async Task SavedStateFailingToLoad_IsRetried_AndPricesBecomeReady() {
        // P4: one failed state load ended the service, so QuoteServiceReady was never set and price pages hung.
        _ = _dbm.SetupSequence(d => d.GetApplicationCommonState(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("db down"), (ApplicationCommonState?)null))
            .ReturnsAsync((Result.SUCCESS, new ApplicationCommonState(false, null, null, null, InstrumentKey.Empty)));
        _reads.Enqueue(new() { ["ABC"] = 1m });
        QuoteService service = await Start();

        await service.QuoteServiceReady.Task.WaitAsync(Deadline);
    }

    [Fact]
    public async Task SymbolMissingFromANewRead_KeepsItsLastPrice() {
        // P5: a read made while GOOGLEFINANCE cells still said "Loading..." replaced the whole cache.
        _reads.Enqueue(new() { ["ABC"] = 1m, ["XYZ"] = 2m }); // the sheet before the refresh
        _reads.Enqueue(new() { ["ABC"] = 1.5m });             // just after: XYZ not computed yet
        QuoteService service = await Start();
        await Eventually(() => _readCount >= 2);

        IDictionary<string, decimal> prices = await Prices(service, "ABC", "XYZ");

        prices.Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1.5m, ["XYZ"] = 2m });
    }

    [Fact]
    public async Task EmptyRead_DoesNotWipeThePrices() {
        _reads.Enqueue(new() { ["ABC"] = 1m });
        _reads.Enqueue([]);
        QuoteService service = await Start();
        await Eventually(() => _readCount >= 2);

        IDictionary<string, decimal> prices = await Prices(service, "ABC");

        prices.Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1m });
    }

    [Fact]
    public async Task PriceRequest_IsAnsweredFromTheCache_WhileTheSheetIsSlow() {
        // R95: the fetch ran inside the input loop, and the clear and writes are synchronous calls that can take
        // 100 s each. On 2026-11-23 a fetch took 11 minutes, and price requests waited behind it until they timed out.
        using var sheetSlow = new ManualResetEventSlim(false);
        int clears = 0;
        _ = _sheets.Setup(s => s.ClearColumns()).Callback(() => { _ = Interlocked.Increment(ref clears); sheetSlow.Wait(Deadline); });
        _reads.Enqueue(new() { ["ABC"] = 1m });   // the sheet before the refresh
        _reads.Enqueue(new() { ["ABC"] = 1.5m }); // after it
        try {
            QuoteService service = await Start();
            await service.QuoteServiceReady.Task.WaitAsync(Deadline);
            await Eventually(() => Volatile.Read(ref clears) == 1); // the fetch is now stuck in the clear

            using var input = new QuoteServiceFillPricesForSymbolsInput(1, null, ["ABC"]);
            service.PostRequest(input).Should().BeTrue();
            object? answered = await input.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            ((IDictionary<string, decimal>)answered!).Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1m });

            sheetSlow.Set();
            await Eventually(() => _readCount >= 2);
            await Eventually(() => service.NextFetchQuotesTime is not null);
            IDictionary<string, decimal> after = await Prices(service, "ABC");
            after.Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1.5m });
        } finally {
            sheetSlow.Set();
        }
    }

    [Fact]
    public async Task PriceNotSeenForLongerThanMaxPriceAge_IsDropped() {
        // A symbol that stops resolving must not keep a stale price forever.
        _reads.Enqueue(new() { ["ABC"] = 1m, ["XYZ"] = 2m });
        _reads.Enqueue(new() { ["ABC"] = 1.5m });
        QuoteService service = await Start(maxPriceAge: TimeSpan.Zero);
        await Eventually(() => _readCount >= 2);

        IDictionary<string, decimal> prices = await Prices(service, "ABC", "XYZ");

        prices.Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1.5m });
    }

    [Fact]
    public async Task FirstReadTimingOut_IsRetriedSoon_AndPricesBecomeReady() {
        // R100: on 2026-11-23 the first read after a restart timed out. It was taken for a shutdown, so nothing was
        // logged as an error, and the company list had no prices until the next fetch time, 33 minutes later.
        SetupSavedState(Result.SUCCESS, nextFetch: DateTime.UtcNow.AddHours(1));
        int reads = 0;
        _ = _sheets.Setup(s => s.ReadQuotes("A", "B", Fallback, It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref reads) == 1
                ? Task.FromException<Dictionary<string, decimal>>(HttpTimeout())
                : Task.FromResult(new Dictionary<string, decimal> { ["ABC"] = 1m }));
        QuoteService service = await Start();

        await service.QuoteServiceReady.Task.WaitAsync(Deadline);

        (await Prices(service, "ABC")).Should().Equal(new Dictionary<string, decimal> { ["ABC"] = 1m });
        _logger.Errors.Should().ContainSingle().Which.Should().BeOfType<TaskCanceledException>();
        _sheets.Verify(s => s.ClearColumns(), Times.Never()); // the retry is of the read; the fetch is still not due
    }

    [Fact]
    public async Task SheetTimingOutWhileAFetchIsDue_IsRetriedWithBackOff_NotInATightLoop() {
        // R100: a timeout set no retry time either, so a fetch that was due started again with no delay.
        int clears = 0;
        _ = _sheets.Setup(s => s.ClearColumns()).Callback(() => Interlocked.Increment(ref clears)).Throws(HttpTimeout());
        _ = await Start();

        await Task.Delay(TimeSpan.FromSeconds(1));

        clears.Should().BeInRange(1, 6);
    }

    [Fact]
    public async Task ShutdownDuringARead_IsACancellation_NotAFailure() {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _sheets.Setup(s => s.ReadQuotes("A", "B", Fallback, It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string[] _, CancellationToken ct) => {
                _ = reading.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return [];
            });
        QuoteService service = await Start();
        await reading.Task.WaitAsync(Deadline);

        await service.StopAsync(CancellationToken.None);

        await Eventually(() => _logger.Messages.Any(m => m.Contains("Operation cancelled")));
        _logger.Errors.Should().BeEmpty();
    }

    private sealed class RecordingLogger : ILogger<QuoteService> {
        private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();
        public IEnumerable<string> Messages => _entries.Select(e => e.Message);
        public IEnumerable<Exception?> Errors => _entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Exception);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
