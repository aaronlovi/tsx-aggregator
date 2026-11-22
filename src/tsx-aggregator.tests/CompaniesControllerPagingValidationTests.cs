using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Moq;
using stock_market_webapi.Controllers;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using static tsx_aggregator.Services.StockDataService;

namespace tsx_aggregator.tests;

/// <summary>
/// Review R80: paging arguments are checked. GET /companies/all with no query string bound both to 0 and returned an
/// empty list claiming about 2.1 billion pages; 0 or negative page numbers silently returned page 1.
/// </summary>
public class CompaniesControllerPagingValidationTests {
    private static AsyncUnaryCall<T> UnaryCall<T>(T reply) => new(
        Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(0, 0)]
    public async Task GetAllCompanies_InvalidPaging_IsBadRequest(int pageNumber, int pageSize) {
        var client = new Mock<StockDataServiceClient>();
        var reply = new GetStocksDataReply { Success = true };
        for (int i = 0; i < 5; i++)
            reply.StocksData.Add(new GetStocksDataReplyItem { Exchange = "TSX", InstrumentSymbol = $"S{i}", CompanySymbol = $"S{i}", CompanyName = "c", InstrumentName = "i", PerSharePrice = 10M, CurrentNumShares = 1000 });
        _ = client.Setup(c => c.GetStocksDataAsync(It.IsAny<GetStocksDataRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(UnaryCall(reply));

        ActionResult<PagedCompanySummaryReports> result = await new CompaniesController(client.Object).GetAllCompanies(pageNumber, pageSize);

        _ = result.Result.Should().BeOfType<BadRequestObjectResult>();
        client.Verify(c => c.GetStocksDataAsync(It.IsAny<GetStocksDataRequest>(), It.IsAny<Metadata>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    public async Task GetMissingDataCompanies_InvalidPaging_IsBadRequest(int pageNumber, int pageSize) {
        var client = new Mock<StockDataServiceClient>(MockBehavior.Strict);

        ActionResult<PagedInstrumentInfoReports> result = await new CompaniesController(client.Object).GetMissingDataCompanies("TSX", pageNumber, pageSize);

        _ = result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
