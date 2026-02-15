using System.Collections.Generic;

namespace tsx_aggregator.models;

public record ReportUpdate(long InstrumentReportId, string MergedReportJson);

public class RawFinancialsDelta {
    private readonly List<CurrentInstrumentRawDataReportDto> _instrumentReportsToInsert;
    private readonly List<CurrentInstrumentRawDataReportDto> _instrumentReportsToObsolete;
    private readonly List<ReportUpdate> _instrumentReportsToUpdate;

    public RawFinancialsDelta(long instrumentId, ulong numShares, decimal pricePerShare) {
        InstrumentId = instrumentId;
        _instrumentReportsToInsert = new();
        _instrumentReportsToObsolete = new();
        _instrumentReportsToUpdate = new();
        NumShares = numShares;
        PricePerShare = pricePerShare;
    }

    public RawFinancialsDelta(RawFinancialsDelta other)
        : this(other.InstrumentId, other.NumShares, other.PricePerShare) {
        _instrumentReportsToInsert = new List<CurrentInstrumentRawDataReportDto>(other.InstrumentReportsToInsert);
        _instrumentReportsToObsolete = new List<CurrentInstrumentRawDataReportDto>(other.InstrumentReportsToObsolete);
        _instrumentReportsToUpdate = new List<ReportUpdate>(other.InstrumentReportsToUpdate);
    }

    public long InstrumentId { get; set; }
    public IList<CurrentInstrumentRawDataReportDto> InstrumentReportsToInsert => _instrumentReportsToInsert;
    public IList<CurrentInstrumentRawDataReportDto> InstrumentReportsToObsolete => _instrumentReportsToObsolete;
    public IList<ReportUpdate> InstrumentReportsToUpdate => _instrumentReportsToUpdate;
    public ulong NumShares { get; set; }
    public decimal PricePerShare { get; set; }
}
