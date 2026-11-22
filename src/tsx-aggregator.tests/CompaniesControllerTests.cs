using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using Moq;
using stock_market_webapi.Controllers;
using tsx_aggregator.Services;
using static tsx_aggregator.Services.StockDataService;

namespace tsx_aggregator.tests;

public class CompaniesControllerTests {
    private static AsyncUnaryCall<T> UnaryCall<T>(T reply) => new(
        Task.FromResult(reply),
        Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess,
        () => [],
        () => { });

    private DateTime? _lastDeadline;
    private CancellationToken _lastToken;

    private (CompaniesController Controller, Func<GetStocksDetailRequest?> LastRequest) CreateController() {
        GetStocksDetailRequest? lastRequest = null;
        var client = new Mock<StockDataServiceClient>();
        _ = client
            .Setup(c => c.GetStocksDetailAsync(
                It.IsAny<GetStocksDetailRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<GetStocksDetailRequest, Metadata, DateTime?, CancellationToken>((req, _, deadline, ct) => {
                lastRequest = req;
                _lastDeadline = deadline;
                _lastToken = ct;
            })
            .Returns(UnaryCall(new GetStocksDetailReply { Success = false }));
        return (new CompaniesController(client.Object), () => lastRequest);
    }

    [Theory]
    [InlineData("TSX", "TSX")]
    [InlineData("tsx", "TSX")]
    [InlineData("TSXV", "TSXV")]
    public async Task GetCompany_SendsTheRouteExchange_UpperCased(string routeExchange, string expectedExchange) {
        var (controller, lastRequest) = CreateController();

        _ = await controller.GetCompany(routeExchange, "LNF");

        _ = lastRequest().Should().NotBeNull();
        _ = lastRequest()!.Exchange.Should().Be(expectedExchange);
        _ = lastRequest()!.InstrumentSymbol.Should().Be("LNF");
    }

    // Review R81: without a deadline and the request's cancellation, a stuck aggregator hung the page forever.
    [Fact]
    public async Task GetCompany_PassesTheRequestCancellationAndADeadlineToTheAggregator() {
        var (controller, _) = CreateController();
        using var request = new CancellationTokenSource();
        DateTime before = DateTime.UtcNow;

        _ = await controller.GetCompany("TSX", "LNF", request.Token);

        _ = _lastToken.Should().Be(request.Token);
        _ = _lastDeadline.Should().NotBeNull();
        _ = _lastDeadline!.Value.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow + CompaniesController.AggregatorCallTimeout);
    }
}
