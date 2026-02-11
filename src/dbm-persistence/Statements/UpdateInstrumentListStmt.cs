using System;
using System.Collections.Generic;
using Npgsql;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace dbm_persistence;

internal class UpdateInstrumentListStmt : NonQueryBatchedDbStmtBase {
    private const string insertSql = "INSERT INTO instruments"
        + " (instrument_id, exchange, company_symbol, company_name, instrument_symbol, instrument_name, created_date, obsoleted_date)"
        + " VALUES (@instrument_id, @exchange, @company_symbol, @company_name, @instrument_symbol, @instrument_name, @created_date, @obsoleted_date)"
        + " ON CONFLICT (exchange, company_symbol, instrument_symbol) DO NOTHING";

    private const string obsoleteSql = "UPDATE instruments SET obsoleted_date = @obsoleted_date"
        + " WHERE instrument_id = @instrument_id";

    // TODO: What about updated companies?
    //       If we track updated companies separately, then we can emit the "UPDATE" company event, not just the "NEW"/"OBSOLETED" events.
    public UpdateInstrumentListStmt(IReadOnlyCollection<InstrumentDto> newInstrumentList, IReadOnlyCollection<InstrumentDto> obsoletedInstrumentList)
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

            AddCommandToBatch(InsertInstrumentEventStmt.sql, new NpgsqlParameter[] {
                new NpgsqlParameter<long>("instrument_id", newInstrument.InstrumentId),
                new NpgsqlParameter<DateTime>("event_date", DateTime.UtcNow),
                new NpgsqlParameter<int>("event_type", (int)CompanyEventTypes.NewListedCompany),
                new NpgsqlParameter<bool>("is_processed", false)
            });
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
}
