using System;
using System.Collections.Generic;
using Npgsql;
using NpgsqlTypes;

namespace dbm_persistence;

/// <summary>
/// After a successful scrape: stamps last_scraped_date, and stores TMX's sector and industry (V5). A scrape without
/// them keeps the stored values.
/// </summary>
internal sealed class UpdateInstrumentScrapeResultStmt : NonQueryDbStmtBase {
    private const string sql = "UPDATE instruments SET last_scraped_date = @last_scraped_date,"
        + " sector = COALESCE(@sector, sector), industry = COALESCE(@industry, industry)"
        + " WHERE instrument_id = @instrument_id";

    private readonly long _instrumentId;
    private readonly DateTime _lastScrapedDate;
    private readonly string? _sector;
    private readonly string? _industry;

    public UpdateInstrumentScrapeResultStmt(long instrumentId, DateTime lastScrapedDate, string? sector, string? industry)
        : base(sql, nameof(UpdateInstrumentScrapeResultStmt)) {
        _instrumentId = instrumentId;
        _lastScrapedDate = lastScrapedDate;
        _sector = sector;
        _industry = industry;
    }

    protected override IReadOnlyCollection<NpgsqlParameter> GetBoundParameters() {
        return new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", _instrumentId),
            new NpgsqlParameter<DateTime>("last_scraped_date", _lastScrapedDate),
            new NpgsqlParameter("sector", NpgsqlDbType.Varchar) { Value = (object?)_sector ?? DBNull.Value },
            new NpgsqlParameter("industry", NpgsqlDbType.Varchar) { Value = (object?)_industry ?? DBNull.Value },
        };
    }
}
