using System.Collections.Generic;

namespace tsx_aggregator.models;
public class RawFinancialsDelta {
    private readonly List<InstrumentReportDto> _instrumentReportsToInsert;
    private readonly List<InstrumentReportDto> _instrumentReportsToObsolete;

    public RawFinancialsDelta(ulong instrumentId, ulong numShares, decimal pricePerShare) {
        InstrumentId = instrumentId;
        _instrumentReportsToInsert = new();
        _instrumentReportsToObsolete = new();
        NumShares = numShares;
        PricePerShare = pricePerShare;
    }

    public RawFinancialsDelta(RawFinancialsDelta other)
        : this(other.InstrumentId, other.NumShares, other.PricePerShare) {
        _instrumentReportsToInsert = new List<InstrumentReportDto>(other.InstrumentReportsToInsert);
        _instrumentReportsToObsolete = new List<InstrumentReportDto>(other.InstrumentReportsToObsolete);
    }

    public ulong InstrumentId { get; set; }
    public IList<InstrumentReportDto> InstrumentReportsToInsert => _instrumentReportsToInsert;
    public IList<InstrumentReportDto> InstrumentReportsToObsolete => _instrumentReportsToObsolete;
    public ulong NumShares { get; set; }
    public decimal PricePerShare { get; set; }
}
