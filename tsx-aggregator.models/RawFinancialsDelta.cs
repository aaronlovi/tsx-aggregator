using System.Collections.Generic;

namespace tsx_aggregator.models;
public class RawFinancialsDelta {
    private readonly List<CurrentInstrumentReportDto> _instrumentReportsToInsert;
    private readonly List<CurrentInstrumentReportDto> _instrumentReportsToObsolete;

    public RawFinancialsDelta(long instrumentId, ulong numShares, decimal pricePerShare) {
        InstrumentId = instrumentId;
        _instrumentReportsToInsert = new();
        _instrumentReportsToObsolete = new();
        NumShares = numShares;
        PricePerShare = pricePerShare;
    }

    public RawFinancialsDelta(RawFinancialsDelta other)
        : this(other.InstrumentId, other.NumShares, other.PricePerShare) {
        _instrumentReportsToInsert = new List<CurrentInstrumentReportDto>(other.InstrumentReportsToInsert);
        _instrumentReportsToObsolete = new List<CurrentInstrumentReportDto>(other.InstrumentReportsToObsolete);
    }

    public long InstrumentId { get; set; }
    public IList<CurrentInstrumentReportDto> InstrumentReportsToInsert => _instrumentReportsToInsert;
    public IList<CurrentInstrumentReportDto> InstrumentReportsToObsolete => _instrumentReportsToObsolete;
    public ulong NumShares { get; set; }
    public decimal PricePerShare { get; set; }
}
