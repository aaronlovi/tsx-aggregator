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

/// <summary>Review R31: the two statements behind tsx-ops merged-reports, against real PostgreSQL.</summary>
public sealed class MergedReportsSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private static readonly DateOnly Mar = new(2022, 3, 31);
    private static readonly DateOnly Dec = new(2022, 12, 31);

    private static string Json(DateOnly date) => $"{{\"A\":1,\"REPORTDATE\":\"{date:yyyy-MM-dd}T00:00:00Z\"}}";

    private async Task Add(long id, long instrumentId, DateOnly date, string? json = null, bool isCurrent = true,
        ReportTypes type = ReportTypes.CashFlow, ReportPeriodTypes period = ReportPeriodTypes.Annual) =>
        await SqlTestRows.AddRawReport(db, id, instrumentId, type, period, date, isCurrent, json ?? Json(date));

    private async Task<IReadOnlyList<MergedReportCandidateDto>> Candidates() {
        (Result res, IReadOnlyList<MergedReportCandidateDto> rows) = await db.Dbm.GetMergedReportCandidates(CancellationToken.None);
        res.Success.Should().BeTrue(res.ErrMsg);
        return [.. rows.OrderBy(r => r.InstrumentReportId)];
    }

    private async Task Reset() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 5, "ABC");
        await SqlTestRows.AddInstrument(db, 6, "XYZ");
    }

    [PostgresFact]
    public async Task Candidates_AreSameYearPairsAndRowsWithAnotherDateInTheirJson() {
        await Reset();
        await Add(1, 5, Mar);
        await Add(2, 5, Dec);                          // a pair
        await Add(3, 5, Dec.AddYears(1));              // another year: no
        await Add(4, 5, Dec, type: ReportTypes.BalanceSheet); // another type: no
        await Add(5, 6, Mar, Json(Dec));               // another date in its JSON
        await Add(6, 6, Dec.AddYears(-1), $"{{\"A\": 1, \"REPORTDATE\": \"2021-06-30T00:00:00Z\"}}"); // the spaced form too
        await Add(7, 6, Dec.AddYears(-2), "{\"A\":1}"); // no date in its JSON: no

        IReadOnlyList<MergedReportCandidateDto> rows = await Candidates();

        rows.Select(r => r.InstrumentReportId).Should().Equal(1, 2, 5, 6);
        rows[0].Should().Be(new MergedReportCandidateDto(1, 5, "ABC", "ABC", (int)ReportTypes.CashFlow, (int)ReportPeriodTypes.Annual, Mar, Json(Mar), false));
    }

    [PostgresFact]
    public async Task Candidates_QuarterlyReportsPairWithinTheQuarter() {
        await Reset();
        await Add(1, 5, new(2025, 6, 30), period: ReportPeriodTypes.Quarterly);
        await Add(2, 5, new(2025, 7, 31), period: ReportPeriodTypes.Quarterly);
        await Add(3, 5, new(2025, 9, 30), period: ReportPeriodTypes.Quarterly);

        (await Candidates()).Select(r => r.InstrumentReportId).Should().Equal(2, 3);
    }

    [PostgresFact]
    public async Task Candidates_LeaveOutReportsThatAreNotCurrentAndDelistedInstruments() {
        await Reset();
        await SqlTestRows.AddInstrument(db, 7, "OLD", obsoletedUtc: SqlTestRows.Created);
        await Add(1, 5, Mar, isCurrent: false);
        await Add(2, 5, Dec);
        await Add(3, 7, Mar);
        await Add(4, 7, Dec);

        (await Candidates()).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Candidates_SayWhichWereResetBefore() {
        await Reset();
        await Add(1, 5, Mar, isCurrent: false);
        await Add(2, 5, Dec);
        await Add(3, 5, Mar); // the clean copy, inserted after the reset

        (await Candidates()).Select(r => (r.InstrumentReportId, r.AlreadyReset)).Should().Equal((2, false), (3, true));
    }

    [PostgresFact]
    public async Task Reset_MarksTheReportsNotCurrentAndQueuesOneEventPerInstrument() {
        await Reset();
        await Add(1, 5, Mar);
        await Add(2, 5, Mar, type: ReportTypes.BalanceSheet);
        await Add(3, 5, Dec);
        await Add(4, 6, Mar);

        Result res = await db.Dbm.ResetRawReports([1, 2], CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_reports WHERE instrument_report_id IN (1, 2) AND NOT is_current AND obsoleted_date IS NOT NULL")).Should().Be(2);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_reports WHERE instrument_report_id IN (3, 4) AND is_current AND obsoleted_date IS NULL")).Should().Be(2);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_events WHERE instrument_id = 5 AND event_type = 4 AND NOT is_processed")).Should().Be(1);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_events")).Should().Be(1);
    }

    [PostgresFact]
    public async Task Reset_OfReportsNoLongerCurrent_ChangesNothing() {
        await Reset();
        await Add(1, 5, Mar, isCurrent: false);

        Result res = await db.Dbm.ResetRawReports([1], CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_events")).Should().Be(0);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM instrument_reports WHERE obsoleted_date = @d", ("d", SqlTestRows.Created.AddDays(1)))).Should().Be(1);
    }
}
