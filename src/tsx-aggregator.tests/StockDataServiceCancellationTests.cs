using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// A gRPC handler must give up when its caller does, even if the service it posted to never answers (code review P6).
/// The handlers used to await the request's Completed task with no cancellation, so a stopped consumer loop hung them.
/// </summary>
public sealed class StockDataServiceCancellationTests {
    private static readonly TimeSpan CallerGivesUpAfter = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private sealed class CancellableCallContext(CancellationToken ct) : ServerCallContext {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => ct;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, []);
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private static StockDataSvc BuildServiceWhoseProcessorNeverAnswers() {
        var processor = new Mock<IStocksDataRequestsProcessor>();
        _ = processor.Setup(p => p.PostRequest(It.IsAny<StocksDataRequestsInputBase>())).Returns(true);
        var quotes = new Mock<IQuoteService>();
        var ready = new TaskCompletionSource();
        ready.SetResult();
        _ = quotes.Setup(q => q.QuoteServiceReady).Returns(ready);

        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<StockDataSvc>>(new NullLogger<StockDataSvc>());
        _ = services.AddSingleton<ILogger<RawCollector>>(new NullLogger<RawCollector>());
        _ = services.AddSingleton<ILogger<RawCollectorFsm>>(new NullLogger<RawCollectorFsm>());
        _ = services.AddSingleton(new Mock<IDbmService>().Object);
        _ = services.AddSingleton(new Registry());
        _ = services.AddSingleton(new Mock<IHttpClientFactory>().Object);
        _ = services.AddSingleton<RawCollector>();
        _ = services.AddSingleton(processor.Object);
        _ = services.AddSingleton(quotes.Object);
        _ = services.AddSingleton(new Mock<ISearchService>().Object);
        return new StockDataSvc(services.BuildServiceProvider());
    }

    [Fact]
    public async Task GetStocksData_WhenTheProcessorNeverAnswers_FailsOnceTheCallerGivesUp() {
        using var caller = new CancellationTokenSource(CallerGivesUpAfter);
        GetStocksDataReply reply = await BuildServiceWhoseProcessorNeverAnswers()
            .GetStocksData(new GetStocksDataRequest { Exchange = "TSX" }, new CancellableCallContext(caller.Token))
            .WaitAsync(Deadline);
        reply.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetStocksDetail_WhenTheProcessorNeverAnswers_FailsOnceTheCallerGivesUp() {
        using var caller = new CancellationTokenSource(CallerGivesUpAfter);
        GetStocksDetailReply reply = await BuildServiceWhoseProcessorNeverAnswers()
            .GetStocksDetail(new GetStocksDetailRequest { Exchange = "TSX", InstrumentSymbol = "ABC" }, new CancellableCallContext(caller.Token))
            .WaitAsync(Deadline);
        reply.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GetDashboardStats_WhenTheProcessorNeverAnswers_FailsOnceTheCallerGivesUp() {
        using var caller = new CancellationTokenSource(CallerGivesUpAfter);
        GetDashboardStatsReply reply = await BuildServiceWhoseProcessorNeverAnswers()
            .GetDashboardStats(new GetDashboardStatsRequest(), new CancellableCallContext(caller.Token))
            .WaitAsync(Deadline);
        reply.Success.Should().BeFalse();
    }
}
