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

    private static (CompaniesController Controller, Func<GetStocksDetailRequest?> LastRequest) CreateController() {
        GetStocksDetailRequest? lastRequest = null;
        var client = new Mock<StockDataServiceClient>();
        _ = client
            .Setup(c => c.GetStocksDetailAsync(
                It.IsAny<GetStocksDetailRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<GetStocksDetailRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) => lastRequest = req)
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
}
