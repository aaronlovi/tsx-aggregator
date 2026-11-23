using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using tsx_aggregator.shared;
using tsx_ops.Db;
using tsx_ops.Snapshots;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// UpdateInstrumentScrapeResultStmt against real PostgreSQL: a successful scrape stamps last_scraped_date and keeps
/// TMX's sector and industry (V5); a scrape without them leaves the stored ones alone.
/// </summary>
public sealed class InstrumentScrapeResultSqlTests(PostgresTestDatabase db) : IClassFixture<PostgresTestDatabase> {
    private static readonly DateTimeOffset Scraped = new(2026, 11, 23, 17, 0, 0, TimeSpan.Zero);

    private async Task<(DateTime? Scraped, string? Sector, string? Industry)> Read(long instrumentId) {
        object? scraped = await db.Scalar("SELECT last_scraped_date FROM instruments WHERE instrument_id = @id", ("id", instrumentId));
        object? sector = await db.Scalar("SELECT sector FROM instruments WHERE instrument_id = @id", ("id", instrumentId));
        object? industry = await db.Scalar("SELECT industry FROM instruments WHERE instrument_id = @id", ("id", instrumentId));
        return (scraped as DateTime?, sector as string, industry as string);
    }

    [PostgresFact]
    public async Task Scrape_StampsTheDate_AndKeepsTheClassification() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "RY");
        await SqlTestRows.AddInstrument(db, 2, "TD");

        Result res = await db.Dbm.UpdateInstrumentScrapeResult(1, Scraped, "Finance", "Banking", CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (DateTime? scraped, string? sector, string? industry) = await Read(1);
        scraped.Should().Be(Scraped.UtcDateTime);
        sector.Should().Be("Finance");
        industry.Should().Be("Banking");
        (await Read(2)).Should().Be(((DateTime?)null, (string?)null, (string?)null), "only the scraped instrument changes");
    }

    [PostgresFact]
    public async Task ScrapeWithoutAClassification_KeepsTheStoredOne() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "RY");
        _ = await db.Dbm.UpdateInstrumentScrapeResult(1, Scraped, "Finance", "Banking", CancellationToken.None);

        Result res = await db.Dbm.UpdateInstrumentScrapeResult(1, Scraped.AddDays(2), null, null, CancellationToken.None);

        res.Success.Should().BeTrue(res.ErrMsg);
        (await Read(1)).Should().Be(((DateTime?)Scraped.AddDays(2).UtcDateTime, "Finance", "Banking"));
    }

    [PostgresFact]
    public async Task Snapshot_ReadsTheClassification() {
        await SqlTestRows.ClearAll(db);
        await SqlTestRows.AddInstrument(db, 1, "RY");
        await SqlTestRows.AddPrice(db, 1, 150m, 1_000_000);
        await SqlTestRows.AddRawReport(db, 11, 1, Constants.ReportTypes.CashFlow, Constants.ReportPeriodTypes.Annual, new DateOnly(2025, 10, 31));
        _ = await db.Dbm.UpdateInstrumentScrapeResult(1, Scraped, "Finance", "Banking", CancellationToken.None);

        List<SnapshotInstrument> instruments = await OpsDb.RunReadOnlyAsync(db.ConnectionString, 30,
            SnapshotSource.LoadInstrumentsAsync, CancellationToken.None);

        SnapshotInstrument ry = instruments.Single();
        ry.Sector.Should().Be("Finance");
        ry.Industry.Should().Be("Banking");
    }
}
