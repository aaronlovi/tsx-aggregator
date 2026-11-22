using System;
using System.Collections.Generic;
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
/// UpdateInstrumentReportsStmt (IDbmService.UpdateRawInstrumentReports) against real PostgreSQL (code review R58):
/// obsoleting, inserting and updating raw reports in one batch, the RawDataChanged event it queues only when reports
/// changed, replacing the instrument's price row, and the batch being all or nothing.
/// </summary>
public sealed class UpdateInstrumentReportsSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private const long InstrumentId = 5;
    private const long OtherInstrumentId = 6;
    private const string OldCashFlowJson = "{\"CashFlowFromOperations\":4200000,\"CapitalExpenditures\":-1100000}";
    private const string MergedBalanceSheetJson = "{\"TotalAssets\":98000000,\"TotalLiabilities\":61000000,\"TotalEquity\":37000000}";
    private const string NewIncomeStatementJson = "{\"NetIncome\":3100000,\"Revenue\":27500000}";

    private static readonly DateOnly Fy2024 = new(2024, 12, 31);
    private static readonly DateOnly Fy2025 = new(2025, 12, 31);

    private async Task Reset() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, InstrumentId, "ABC");
        await SqlTestRows.AddInstrument(db, OtherInstrumentId, "XYZ");
        await SqlTestRows.AddPrice(db, InstrumentId, 18.40m, 52_000_000);
        await SqlTestRows.AddPrice(db, OtherInstrumentId, 2.10m, 9_000_000);
        await SqlTestRows.AddRawReport(db, 101, InstrumentId, ReportTypes.CashFlow, ReportPeriodTypes.Annual, Fy2024, reportJson: OldCashFlowJson);
        await SqlTestRows.AddRawReport(db, 102, InstrumentId, ReportTypes.BalanceSheet, ReportPeriodTypes.Annual, Fy2024, reportJson: "{\"TotalAssets\":98000000}");
    }

    private static CurrentInstrumentRawDataReportDto Report(long reportId, ReportTypes type, DateOnly date, string json) =>
        new(reportId, InstrumentId, (int)type, (int)ReportPeriodTypes.Annual, json, date);

    private static RawFinancialsDelta Delta(decimal price = 19.05m, ulong shares = 52_500_000) => new(InstrumentId, shares, price);

    private async Task<long> PendingRawDataChangedEvents() =>
        await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_events WHERE instrument_id = @id AND event_type = 4 AND NOT is_processed",
            ("id", InstrumentId));

    private async Task<IReadOnlyList<CurrentInstrumentRawDataReportDto>> CurrentReports() {
        (Result res, IReadOnlyList<CurrentInstrumentRawDataReportDto> reports) = await db.Dbm.GetRawFinancialsByInstrumentId(InstrumentId, CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return reports;
    }

    [PostgresFact]
    public async Task ObsoleteInsertAndUpdate_InOneBatch() {
        await Reset();
        RawFinancialsDelta delta = Delta();
        delta.InstrumentReportsToObsolete.Add(Report(101, ReportTypes.CashFlow, Fy2024, OldCashFlowJson));
        delta.InstrumentReportsToInsert.Add(Report(201, ReportTypes.IncomeStatement, Fy2025, NewIncomeStatementJson));
        delta.InstrumentReportsToUpdate.Add(new ReportUpdate(102, MergedBalanceSheetJson));

        Result res = await db.Dbm.UpdateRawInstrumentReports(delta, CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        IReadOnlyList<CurrentInstrumentRawDataReportDto> current = await CurrentReports();
        current.Should().BeEquivalentTo([
            new CurrentInstrumentRawDataReportDto(102, InstrumentId, (int)ReportTypes.BalanceSheet, (int)ReportPeriodTypes.Annual, MergedBalanceSheetJson, Fy2024),
            Report(201, ReportTypes.IncomeStatement, Fy2025, NewIncomeStatementJson),
        ]);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_reports WHERE instrument_report_id = 101 AND NOT is_current AND obsoleted_date IS NOT NULL"))
            .Should().Be(1, "the obsoleted report is kept, marked not current");
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_reports WHERE instrument_report_id = 102 AND created_date > @created",
            ("created", SqlTestRows.Created))).Should().Be(1, "an updated report gets a new created date");
        (await PendingRawDataChangedEvents()).Should().Be(1);
    }

    [PostgresFact]
    public async Task OnlyAnUpdate_StillQueuesARawDataChangedEvent() {
        await Reset();
        RawFinancialsDelta delta = Delta();
        delta.InstrumentReportsToUpdate.Add(new ReportUpdate(102, MergedBalanceSheetJson));

        _ = await db.Dbm.UpdateRawInstrumentReports(delta, CancellationToken.None);

        (await PendingRawDataChangedEvents()).Should().Be(1);
    }

    [PostgresFact]
    public async Task NoReportChanges_QueuesNoEvent_ButReplacesThePrice() {
        await Reset();

        Result res = await db.Dbm.UpdateRawInstrumentReports(Delta(price: 19.05m, shares: 52_500_000), CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await PendingRawDataChangedEvents()).Should().Be(0);
        (await CurrentReports()).Select(r => r.InstrumentReportId).Should().BeEquivalentTo([101L, 102L]);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_prices WHERE instrument_id = @id AND obsoleted_date IS NULL"
            + " AND price_per_share = 19.05 AND num_shares = 52500000", ("id", InstrumentId))).Should().Be(1);
    }

    [PostgresFact]
    public async Task PriceRows_TheOldOneIsObsoleted_AndOtherInstrumentsAreUntouched() {
        await Reset();
        RawFinancialsDelta delta = Delta(price: 19.05m, shares: 52_500_000);
        delta.InstrumentReportsToInsert.Add(Report(201, ReportTypes.IncomeStatement, Fy2025, NewIncomeStatementJson));

        _ = await db.Dbm.UpdateRawInstrumentReports(delta, CancellationToken.None);

        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_prices WHERE instrument_id = @id AND obsoleted_date IS NULL", ("id", InstrumentId)))
            .Should().Be(1, "exactly one current price row per instrument");
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_prices WHERE instrument_id = @id AND obsoleted_date IS NOT NULL"
            + " AND price_per_share = 18.40", ("id", InstrumentId))).Should().Be(1);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_prices WHERE instrument_id = @id AND obsoleted_date IS NULL"
            + " AND price_per_share = 2.10", ("id", OtherInstrumentId))).Should().Be(1);

        (await db.Dbm.GetNextInstrumentEvent(CancellationToken.None)).Item2!.PricePerShare
            .Should().Be(19.05m, "the queued event is read with the new price");
    }

    [PostgresFact]
    public async Task AFailingCommand_RollsBackTheWholeBatch() {
        await Reset();
        RawFinancialsDelta delta = Delta();
        delta.InstrumentReportsToObsolete.Add(Report(101, ReportTypes.CashFlow, Fy2024, OldCashFlowJson));
        delta.InstrumentReportsToInsert.Add(Report(102, ReportTypes.IncomeStatement, Fy2025, NewIncomeStatementJson)); // duplicate id

        Result res = await db.Dbm.UpdateRawInstrumentReports(delta, CancellationToken.None);

        res.Success.Should().BeFalse();
        (await CurrentReports()).Select(r => r.InstrumentReportId).Should().BeEquivalentTo([101L, 102L]);
        (await PendingRawDataChangedEvents()).Should().Be(0);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_prices WHERE instrument_id = @id AND obsoleted_date IS NULL"
            + " AND price_per_share = 18.40", ("id", InstrumentId))).Should().Be(1);
    }
}
