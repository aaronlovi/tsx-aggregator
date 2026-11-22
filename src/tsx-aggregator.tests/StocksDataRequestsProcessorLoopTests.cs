using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// A request that throws must fail only that request (code review P2). The exception used to escape ExecuteAsync,
/// which stops the whole host: a DB error, or a page request cancelled at the wrong moment, took every service down.
/// </summary>
public sealed class StocksDataRequestsProcessorLoopTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private StocksDataRequestsProcessor? _processor;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        if (_processor is not null) {
            await _processor.StopAsync(CancellationToken.None);
            _processor.Dispose();
        }
    }

    private async Task<StocksDataRequestsProcessor> StartProcessor() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<StocksDataRequestsProcessor>>(new NullLogger<StocksDataRequestsProcessor>());
        _ = services.AddSingleton(_dbm.Object);
        _processor = new StocksDataRequestsProcessor(services.BuildServiceProvider());
        await _processor.StartAsync(CancellationToken.None);
        return _processor;
    }

    private void SetupStocksDataThen(Exception first) =>
        _dbm.SetupSequence(d => d.GetProcessedStockDataByExchange(Constants.TsxExchange, It.IsAny<CancellationToken>()))
            .ThrowsAsync(first)
            .ReturnsAsync(Result<IReadOnlyList<ProcessedFullInstrumentReportDto>>.SetSuccess([]));

    private static async Task ExpectServed(StocksDataRequestsProcessor processor) {
        using var ok = new GetStocksForExchangeRequest(2, Constants.TsxExchange, null);
        processor.PostRequest(ok).Should().BeTrue();
        object? reply = await ok.Completed.Task.WaitAsync(Deadline);
        reply.Should().BeOfType<GetStocksDataReply>().Which.Success.Should().BeTrue();
        processor.ExecuteTask!.IsCompleted.Should().BeFalse("the processor must still be running");
    }

    [Fact]
    public async Task RequestWhoseDbCallThrows_FailsThatRequestOnly() {
        SetupStocksDataThen(new InvalidOperationException("db exploded"));
        StocksDataRequestsProcessor processor = await StartProcessor();

        using var failing = new GetStocksForExchangeRequest(1, Constants.TsxExchange, null);
        processor.PostRequest(failing).Should().BeTrue();
        Func<Task> wait = () => failing.Completed.Task.WaitAsync(Deadline);
        _ = await wait.Should().ThrowAsync<InvalidOperationException>().WithMessage("db exploded");

        await ExpectServed(processor);
    }

    [Fact]
    public async Task RequestCancelledByItsCaller_IsCancelledAndTheLoopKeepsRunning() {
        SetupStocksDataThen(new OperationCanceledException());
        StocksDataRequestsProcessor processor = await StartProcessor();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var cancelled = new GetStocksForExchangeRequest(1, Constants.TsxExchange, cts);
        processor.PostRequest(cancelled).Should().BeTrue();
        Func<Task> wait = () => cancelled.Completed.Task.WaitAsync(Deadline);
        _ = await wait.Should().ThrowAsync<OperationCanceledException>();

        await ExpectServed(processor);
    }
}
