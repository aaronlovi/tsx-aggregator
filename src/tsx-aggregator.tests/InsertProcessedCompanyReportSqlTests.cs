using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// InsertProcessedCompanyReportStmt against real PostgreSQL (code review R58): a new processed report obsoletes the
/// instrument's current one and becomes the only current report, without touching other instruments' reports or the
/// dates of reports obsoleted earlier.
/// </summary>
public sealed class InsertProcessedCompanyReportSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private const long InstrumentId = 11;
    private const long OtherInstrumentId = 12;
    private static readonly DateTime FirstAggregation = new(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondAggregation = new(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc);

    private async Task Reset() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, InstrumentId, "ABC");
        await SqlTestRows.AddInstrument(db, OtherInstrumentId, "XYZ");
    }

    private Task<Result> Insert(long instrumentId, string json, DateTime createdUtc) =>
        db.Dbm.InsertProcessedCompanyReport(new ProcessedInstrumentReportDto(instrumentId, json, new DateTimeOffset(createdUtc), null), CancellationToken.None).AsTask();

    private Task<long> CurrentReports(long instrumentId) =>
        SqlTestRows.Count(db, "SELECT count(*) FROM processed_instrument_reports WHERE instrument_id = @id AND obsoleted_date IS NULL", ("id", instrumentId));

    [PostgresFact]
    public async Task FirstReport_IsInsertedAsCurrent() {
        await Reset();

        Result res = await Insert(InstrumentId, "{\"score\":12}", FirstAggregation);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await db.Scalar("SELECT report_json FROM processed_instrument_reports WHERE instrument_id = @id AND obsoleted_date IS NULL", ("id", InstrumentId)))
            .Should().Be("{\"score\":12}");
        (await db.Scalar("SELECT created_date FROM processed_instrument_reports WHERE instrument_id = @id", ("id", InstrumentId)))
            .Should().Be(FirstAggregation);
    }

    [PostgresFact]
    public async Task SecondReport_ObsoletesThePreviousOne() {
        await Reset();
        _ = await Insert(InstrumentId, "{\"score\":12}", FirstAggregation);

        Result res = await Insert(InstrumentId, "{\"score\":14}", SecondAggregation);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await CurrentReports(InstrumentId)).Should().Be(1);
        (await db.Scalar("SELECT report_json FROM processed_instrument_reports WHERE instrument_id = @id AND obsoleted_date IS NULL", ("id", InstrumentId)))
            .Should().Be("{\"score\":14}");
        (await SqlTestRows.Count(db, "SELECT count(*) FROM processed_instrument_reports WHERE instrument_id = @id"
            + " AND report_json = '{\"score\":12}' AND obsoleted_date IS NOT NULL", ("id", InstrumentId))).Should().Be(1);
    }

    [PostgresFact]
    public async Task OtherInstrumentsReports_StayCurrent() {
        await Reset();
        _ = await Insert(OtherInstrumentId, "{\"score\":9}", FirstAggregation);

        _ = await Insert(InstrumentId, "{\"score\":14}", SecondAggregation);

        (await CurrentReports(OtherInstrumentId)).Should().Be(1);
    }

    [PostgresFact]
    public async Task EarlierObsoletedReports_KeepTheirObsoletedDate() {
        await Reset();
        DateTime obsoletedEarlier = FirstAggregation.AddDays(-30);
        await SqlTestRows.AddProcessedReport(db, InstrumentId, "{\"score\":10}", FirstAggregation.AddDays(-60), obsoletedEarlier);
        _ = await Insert(InstrumentId, "{\"score\":12}", FirstAggregation);

        _ = await Insert(InstrumentId, "{\"score\":14}", SecondAggregation);

        (await db.Scalar("SELECT obsoleted_date FROM processed_instrument_reports WHERE report_json = '{\"score\":10}'"))
            .Should().Be(obsoletedEarlier);
        (await SqlTestRows.Count(db, "SELECT count(*) FROM processed_instrument_reports WHERE instrument_id = @id", ("id", InstrumentId)))
            .Should().Be(3);
    }
}
