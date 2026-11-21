using System;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

/// <summary>
/// B13: the dashboard's processed-report counts cover listed instruments only. A delisted instrument keeps its
/// processed report, which made "without processed reports" (listed minus with) go negative.
/// </summary>
public class DashboardStatsCountTests {
    private static IDbmService NewDbm() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<DbmInMemory>>(new NullLogger<DbmInMemory>());
        return new DbmInMemory(services.BuildServiceProvider());
    }

    private static InstrumentDto Instrument(long id, string symbol) =>
        new(id, "TSX", symbol, symbol + " Inc.", symbol, symbol + " Common", DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task GetDashboardStats_DelistedInstrumentWithProcessedReport_IsNotCounted() {
        IDbmService dbm = NewDbm();
        InstrumentDto listedWithReport = Instrument(1, "AAA");
        InstrumentDto listedWithoutReport = Instrument(2, "BBB");
        InstrumentDto delistedWithReport = Instrument(3, "CCC");
        _ = await dbm.UpdateInstrumentList([listedWithReport, listedWithoutReport, delistedWithReport], [], [], CancellationToken.None);
        _ = await dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(1, "{}", DateTimeOffset.UtcNow, null), CancellationToken.None);
        _ = await dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(3, "{}", DateTimeOffset.UtcNow, null), CancellationToken.None);
        _ = await dbm.UpdateInstrumentList([], [], [delistedWithReport], CancellationToken.None);

        (Result res, DashboardStatsDto? stats) = await dbm.GetDashboardStats(CancellationToken.None);

        _ = res.Success.Should().BeTrue();
        _ = stats!.TotalActiveInstruments.Should().Be(2);
        _ = stats.InstrumentsWithProcessedReports.Should().Be(1);
        _ = stats.InstrumentsWithoutProcessedReports.Should().Be(1);
    }
}
