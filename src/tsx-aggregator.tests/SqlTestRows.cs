using System;
using System.Threading.Tasks;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

/// <summary>
/// Plain-SQL row setup for the tests of the real statements against <see cref="PostgresTestDatabase"/> (code review
/// R58, R59). Rows go in directly so that each test controls dates, obsoleted flags and ids exactly.
/// </summary>
internal static class SqlTestRows {
    public static readonly DateTime Created = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static object OrNull(DateTime? value) => value is DateTime d ? d : DBNull.Value;

    /// <summary>Empties every table that references an instrument, then the instruments themselves (foreign keys).</summary>
    public static async Task ClearAll(PostgresTestDatabase db) =>
        _ = await db.Scalar("DELETE FROM instrument_events; DELETE FROM instrument_prices; DELETE FROM instrument_reports;"
            + " DELETE FROM processed_instrument_reports; DELETE FROM raw_instrument_processing_state; DELETE FROM instruments");

    public static async Task AddInstrument(PostgresTestDatabase db, long instrumentId, string symbol,
        string exchange = Constants.TsxExchange, DateTime? obsoletedUtc = null, DateTime? lastScrapedUtc = null) =>
        _ = await db.Scalar("INSERT INTO instruments (instrument_id, exchange, company_symbol, company_name, instrument_symbol,"
            + " instrument_name, created_date, obsoleted_date, last_scraped_date)"
            + " VALUES (@id, @exchange, @symbol, @company_name, @symbol, @instrument_name, @created, @obsoleted, @scraped)",
            ("id", instrumentId), ("exchange", exchange), ("symbol", symbol), ("company_name", symbol + " Inc."),
            ("instrument_name", symbol + " Common Shares"), ("created", Created), ("obsoleted", OrNull(obsoletedUtc)),
            ("scraped", OrNull(lastScrapedUtc)));

    public static async Task AddPrice(PostgresTestDatabase db, long instrumentId, decimal pricePerShare, long numShares,
        DateTime? obsoletedUtc = null) =>
        _ = await db.Scalar("INSERT INTO instrument_prices (instrument_id, price_per_share, num_shares, created_date, obsoleted_date)"
            + " VALUES (@id, @price, @shares, @created, @obsoleted)",
            ("id", instrumentId), ("price", pricePerShare), ("shares", numShares), ("created", Created), ("obsoleted", OrNull(obsoletedUtc)));

    public static async Task AddEvent(PostgresTestDatabase db, long instrumentId, DateTime eventDateUtc,
        Constants.CompanyEventTypes eventType, bool isProcessed = false) =>
        _ = await db.Scalar("INSERT INTO instrument_events (instrument_id, event_date, event_type, is_processed)"
            + " VALUES (@id, @date, @type, @processed)",
            ("id", instrumentId), ("date", eventDateUtc), ("type", (int)eventType), ("processed", isProcessed));

    public static async Task AddRawReport(PostgresTestDatabase db, long instrumentReportId, long instrumentId,
        Constants.ReportTypes reportType, Constants.ReportPeriodTypes periodType, DateOnly reportDate,
        bool isCurrent = true, string reportJson = "{\"NetIncome\":1250000}", DateTime? createdUtc = null) =>
        _ = await db.Scalar("INSERT INTO instrument_reports (instrument_report_id, instrument_id, report_type, report_period_type,"
            + " report_json, report_date, created_date, obsoleted_date, is_current)"
            + " VALUES (@rid, @id, @type, @period, @json, @date, @created, @obsoleted, @current)",
            ("rid", instrumentReportId), ("id", instrumentId), ("type", (int)reportType), ("period", (int)periodType),
            ("json", reportJson), ("date", reportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            ("created", createdUtc ?? Created), ("obsoleted", isCurrent ? DBNull.Value : Created.AddDays(1)), ("current", isCurrent));

    public static async Task AddProcessedReport(PostgresTestDatabase db, long instrumentId, string reportJson,
        DateTime createdUtc, DateTime? obsoletedUtc = null) =>
        _ = await db.Scalar("INSERT INTO processed_instrument_reports (instrument_id, report_json, created_date, obsoleted_date)"
            + " VALUES (@id, @json, @created, @obsoleted)",
            ("id", instrumentId), ("json", reportJson), ("created", createdUtc), ("obsoleted", OrNull(obsoletedUtc)));

    public static async Task<long> Count(PostgresTestDatabase db, string sql, params (string Name, object Value)[] parameters) =>
        (long)(await db.Scalar(sql, parameters))!;
}
