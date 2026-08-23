using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal class UpdateInstrumentListStmt : NonQueryBatchedDbStmtBase {
    // No ON CONFLICT clause: callers must route an instrument that already has a row
    // through the revive path instead. Swallowing the conflict here would silently drop
    // the instrument while still emitting its NewListedCompany event.
    private const string insertSql = "INSERT INTO instruments"
        + " (instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date, obsoleted_date)"
        + " VALUES (@instrument_id, @exchange, @company_symbol, @company_name, @instrument_symbol, @instrument_name, @created_date, @obsoleted_date)";

    // Re-lists a previously obsoleted instrument by clearing its obsoleted_date in place.
    // The row keeps its instrument_id, so the reports already scraped for it stay attached.
    private const string reviveSql = "UPDATE instruments"
        + " SET obsoleted_date = null, company_name = @company_name, instrument_name = @instrument_name"
        + " WHERE instrument_id = @instrument_id";

    private const string obsoleteSql = "UPDATE instruments SET obsoleted_date = @obsoleted_date"
        + " WHERE instrument_id = @instrument_id";

    // TODO: What about updated companies?
    //       If we track updated companies separately, then we can emit the "UPDATE" company event, not just the "NEW"/"OBSOLETED" events.
    public UpdateInstrumentListStmt(
        IReadOnlyCollection<InstrumentDto> newInstrumentList,
        IReadOnlyCollection<InstrumentDto> revivedInstrumentList,
        IReadOnlyCollection<InstrumentDto> obsoletedInstrumentList)
        : base(nameof(UpdateInstrumentListStmt))
    {
        foreach (InstrumentDto newInstrument in newInstrumentList) {
            AddCommandToBatch(insertSql, new NpgsqlParameter[] {
                new NpgsqlParameter<long>("instrument_id", newInstrument.InstrumentId),
                new NpgsqlParameter<string>("exchange", newInstrument.Exchange),
                new NpgsqlParameter<string>("company_symbol", newInstrument.CompanySymbol),
                new NpgsqlParameter<string>("company_name", newInstrument.CompanyName),
                new NpgsqlParameter<string>("instrument_symbol", newInstrument.InstrumentSymbol),
                new NpgsqlParameter<string>("instrument_name", newInstrument.InstrumentName),
                new NpgsqlParameter<DateTime>("created_date", DateTime.UtcNow),
                DbUtils.CreateNullableDateTimeParam("obsoleted_date", newInstrument.ObsoletedDate)
            });

            AddNewListedCompanyEventToBatch(newInstrument.InstrumentId);
        }

        foreach (InstrumentDto revivedInstrument in revivedInstrumentList) {
            AddCommandToBatch(reviveSql, new NpgsqlParameter[] {
                new NpgsqlParameter<string>("company_name", revivedInstrument.CompanyName),
                new NpgsqlParameter<string>("instrument_name", revivedInstrument.InstrumentName),
                new NpgsqlParameter<long>("instrument_id", revivedInstrument.InstrumentId)
            });

            // A re-listed instrument is new as far as downstream aggregation is concerned:
            // its reports need reprocessing.
            AddNewListedCompanyEventToBatch(revivedInstrument.InstrumentId);
        }

        foreach (InstrumentDto obsoletedInstrument in obsoletedInstrumentList) {
            AddCommandToBatch(obsoleteSql, new NpgsqlParameter[] {
                new NpgsqlParameter<DateTime>("obsoleted_date", DateTime.UtcNow),
                new NpgsqlParameter<long>("instrument_id", obsoletedInstrument.InstrumentId)
            });

            AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
                new NpgsqlParameter<long>("instrument_id", obsoletedInstrument.InstrumentId),
                new NpgsqlParameter<DateTime>("event_date", DateTime.UtcNow),
                new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.ObsoletedListedCompany),
                new NpgsqlParameter<bool>("is_processed", false)
            });
        }
    }

    private void AddNewListedCompanyEventToBatch(long instrumentId) {
        AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
            new NpgsqlParameter<long>("instrument_id", instrumentId),
            new NpgsqlParameter<DateTime>("event_date", DateTime.UtcNow),
            new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.NewListedCompany),
            new NpgsqlParameter<bool>("is_processed", false)
        });
    }
}
