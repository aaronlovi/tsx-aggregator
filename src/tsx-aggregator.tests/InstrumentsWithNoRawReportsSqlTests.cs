using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>
/// GetInstrumentsWithNoRawReportsStmt against real PostgreSQL (code review R58; the in-memory fake always returns an
/// empty page): listed instruments of the exchange with no raw report at all, ordered by instrument symbol and paged,
/// with the total across all pages.
/// </summary>
public sealed class InstrumentsWithNoRawReportsSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private async Task Reset() {
        await SqlTestRows.ClearAll(db);
        // Five without raw reports, inserted out of symbol order.
        await SqlTestRows.AddInstrument(db, 1, "EEE");
        await SqlTestRows.AddInstrument(db, 2, "AAA");
        await SqlTestRows.AddInstrument(db, 3, "CCC");
        await SqlTestRows.AddInstrument(db, 4, "BBB");
        await SqlTestRows.AddInstrument(db, 5, "DDD");
        // Left out: has raw reports (even only an obsoleted one), delisted, other exchange.
        await SqlTestRows.AddInstrument(db, 6, "ABB");
        await SqlTestRows.AddRawReport(db, 61, 6, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31));
        await SqlTestRows.AddRawReport(db, 62, 6, ReportTypes.BalanceSheet, ReportPeriodTypes.Annual, new DateOnly(2025, 12, 31));
        await SqlTestRows.AddInstrument(db, 7, "ACC");
        await SqlTestRows.AddRawReport(db, 71, 7, ReportTypes.CashFlow, ReportPeriodTypes.Annual, new DateOnly(2024, 12, 31), isCurrent: false);
        await SqlTestRows.AddInstrument(db, 8, "ADD", obsoletedUtc: SqlTestRows.Created.AddDays(10));
        await SqlTestRows.AddInstrument(db, 9, "AEE", exchange: "TSXV");
    }

    private async Task<PagedInstrumentInfoDto> Page(int pageNumber, int pageSize, string exchange = TsxExchange) {
        Result<PagedInstrumentInfoDto> res = await db.Dbm.GetInstrumentsWithNoRawReports(exchange, pageNumber, pageSize, CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return res.Data!;
    }

    [PostgresFact]
    public async Task FirstPage_IsTheFirstSymbolsInOrder_WithTheTotal() {
        await Reset();

        PagedInstrumentInfoDto page = await Page(1, 2);

        page.PageNumber.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalInstruments.Should().Be(5);
        page.Instruments.Select(i => i.InstrumentSymbol).Should().Equal("AAA", "BBB");
        page.Instruments[0].Should().Be(new InstrumentInfoDto(2, TsxExchange, "AAA", "AAA", "AAA Inc.", "AAA Common Shares"));
    }

    [PostgresFact]
    public async Task LaterPages_ContinueInOrder_AndTheLastPageIsPartial() {
        await Reset();

        (await Page(2, 2)).Instruments.Select(i => i.InstrumentSymbol).Should().Equal("CCC", "DDD");
        PagedInstrumentInfoDto last = await Page(3, 2);
        last.Instruments.Select(i => i.InstrumentSymbol).Should().Equal("EEE");
        last.TotalInstruments.Should().Be(5);
    }

    [PostgresFact]
    public async Task PagePastTheEnd_IsEmpty() {
        await Reset();

        PagedInstrumentInfoDto page = await Page(4, 2);

        page.Instruments.Should().BeEmpty();
        page.PageNumber.Should().Be(4);
    }

    [PostgresFact]
    public async Task OtherExchange_IsPagedSeparately() {
        await Reset();

        PagedInstrumentInfoDto page = await Page(1, 10, "TSXV");

        page.TotalInstruments.Should().Be(1);
        page.Instruments.Select(i => i.InstrumentSymbol).Should().Equal("AEE");
    }
}
