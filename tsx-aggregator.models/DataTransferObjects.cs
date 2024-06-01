using System;
using System.Collections.Generic;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public record InstrumentEventDto(
    long InstrumentId,
    DateTimeOffset EventDate,
    int EventType,
    bool IsProcessed);

public record InstrumentEventExDto(
    InstrumentEventDto InstrumentEvent,
    string InstrumentSymbol,
    string InstrumentName,
    string Exchange,
    decimal PricePerShare,
    long NumShares) {
    public long InstrumentId => InstrumentEvent.InstrumentId;
    public DateTimeOffset EventDate => InstrumentEvent.EventDate;
    public int EventType => InstrumentEvent.EventType;
    public bool IsProcessed => InstrumentEvent.IsProcessed;
}

public record CurrentInstrumentReportDto(
    long InstrumentReportId,
    long InstrumentId,
    int ReportType,
    int ReportPeriodType,
    string ReportJson,
    DateOnly ReportDate);

public record ProcessedInstrumentReportDto(
    long InstrumentId,
    string SerializedReport,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ObsoletedDate);

public record ProcessedFullInstrumentReportDto(
    long InstrumentId,
    string CompanySymbol,
    string CompanyName,
    string InstrumentSymbol,
    string InstrumentName,
    string SerializedReport,
    DateTimeOffset InstrumentCreatedDate,
    DateTimeOffset? InstrumentObsoletedDate,
    DateTimeOffset ReportCreatedDate,
    DateTimeOffset? ReportObsoletedDate,
    int NumAnnualCashFlowReports);

public record InstrumentDto : InstrumentKey {
    public InstrumentDto(
        long instrumentId,
        string exchange,
        string companySymbol,
        string companyName,
        string instrumentSymbol,
        string instrumentName,
        DateTimeOffset createdDate,
        DateTimeOffset? obsoletedDate)
        : base(companySymbol, instrumentSymbol, exchange) {
        InstrumentId = instrumentId;
        Exchange = exchange;
        CompanyName = companyName;
        InstrumentName = instrumentName;
        CreatedDate = createdDate;
        ObsoletedDate = obsoletedDate;
    }

    public long InstrumentId { get; init; }
    public string CompanyName { get; init; }
    public string InstrumentName { get; init; }
    public DateTimeOffset CreatedDate { get; init; }
    public DateTimeOffset? ObsoletedDate { get; init; }

    public bool IsTsxPeferredShares {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            if (CompanySymbol.Contains(".PR.", StringComparison.Ordinal))
                return true;
            if (CompanySymbol.EndsWith(".PR", StringComparison.Ordinal))
                return true;
            if (CompanySymbol.Contains(".PF."))
                return true;
            if (CompanySymbol.EndsWith(".PF", StringComparison.Ordinal))
                return true;
            if (InstrumentSymbol.Contains(".PR."))
                return true;
            if (InstrumentSymbol.EndsWith(".PR", StringComparison.Ordinal))
                return true;
            if (InstrumentSymbol.Contains(".PF."))
                return true;
            if (InstrumentSymbol.EndsWith(".PF", StringComparison.Ordinal))
                return true;
            return false;
        }
    }

    public bool IsTsxWarrant {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            if (CompanySymbol.Contains(".WT."))
                return true;
            if (CompanySymbol.EndsWith(".WT", StringComparison.Ordinal))
                return true;
            if (InstrumentSymbol.Contains(".WT."))
                return true;
            if (InstrumentSymbol.EndsWith(".WT", StringComparison.Ordinal))
                return true;
            if (InstrumentSymbol.Contains(".WS."))
                return true;
            if (InstrumentSymbol.EndsWith(".WS", StringComparison.Ordinal))
                return true;
            return false;
        }
    }

    public bool IsTsxCompanyBonds {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            if (CompanySymbol.Contains(".DB."))
                return true;
            if (CompanySymbol.EndsWith(".DB", StringComparison.Ordinal))
                return true;
            if (InstrumentSymbol.Contains(".DB."))
                return true;
            if (InstrumentSymbol.EndsWith(".DB", StringComparison.Ordinal))
                return true;
            return false;
        }
    }

    public bool IsTsxETF {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            if (CompanyName.Contains(" ETF"))
                return true;
            if (InstrumentName.Contains(" ETF"))
                return true;
            return false;
        }
    }

    public bool IsTsxBmoMutualFund {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            return CompanyName.StartsWith("BMO", StringComparison.Ordinal) && CompanyName.EndsWith(" Fund", StringComparison.Ordinal);
        }
    }

    public bool IsTsxMutualFund {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            return CompanyName.EndsWith(" Fund", StringComparison.Ordinal);
        }
    }

    public bool IsPimcoMutualFund {
        get {
            if (!Exchange.Equals(Constants.TsxExchange, StringComparison.Ordinal))
                return false;
            return CompanyName.StartsWith("PIMCO", StringComparison.Ordinal);
        }
    }
}

public record InstrumentKey(string CompanySymbol, string InstrumentSymbol, string Exchange) {
    public static readonly InstrumentKey Empty = new(string.Empty, string.Empty, string.Empty);
    public static readonly ComparerBySymbols_ ComparerBySymbols = new();

    public static int CompareBySymbols(InstrumentKey k1, InstrumentKey k2) {
        int res = k1.CompanySymbol.CompareTo(k2.CompanySymbol);
        if (res == 0)
            res = k1.InstrumentSymbol.CompareTo(k2.InstrumentSymbol);
        if (res == 0)
            res = k1.Exchange.CompareTo(k2.Exchange);
        return res;
    }

    public class ComparerBySymbols_ : IComparer<InstrumentKey> {
        public int Compare(InstrumentKey? x, InstrumentKey? y) {
            if (x is null && y is null)
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            return CompareBySymbols(x, y);
        }
    }
}

public record InstrumentPriceDto(
    long InstrumentId,
    decimal PricePerShare,
    long NumShares,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ObsoletedDate);

public record InstrumentReportDto(
    long InstrumentReportId,
    long InstrumentId,
    int ReportType,
    int ReportPeriodType,
    string ReportJson,
    DateOnly ReportDate,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ObsoletedDate,
    bool IsCurrent);

public class StateFsmState {
    private DateTime? _nextFetchDirectoryTime;
    private DateTime? _nextFetchInstrumentDataTime;
    private DateTime? _nextFetchQuotesTime;
    private InstrumentKey _prevInstrumentKey;

    public StateFsmState() : this(null, null, null, InstrumentKey.Empty) { }

    public StateFsmState(
        DateTime? nextFetchDirectoryTime,
        DateTime? nextFetchInstrumentDataTime,
        DateTime? nextFetchQuotesTime,
        InstrumentKey prevInstrumentKey) {
        _nextFetchDirectoryTime = nextFetchDirectoryTime;
        _nextFetchInstrumentDataTime = nextFetchInstrumentDataTime;
        _nextFetchQuotesTime = nextFetchQuotesTime;
        _prevInstrumentKey = prevInstrumentKey;
    }

    public StateFsmState(StateFsmState other)
        : this(other.NextFetchDirectoryTime, other.NextFetchInstrumentDataTime, other.NextFetchQuotesTime, other.PrevInstrumentKey) {
    }

    public bool IsDirty { get; private set; }

    public DateTime? NextFetchDirectoryTime {
        get => _nextFetchDirectoryTime;
        set {
            _nextFetchDirectoryTime = value;
            IsDirty = true;
        }
    }

    public DateTime? NextFetchInstrumentDataTime {
        get => _nextFetchInstrumentDataTime;
        set {
            _nextFetchInstrumentDataTime = value;
            IsDirty = true;
        }
    }

    public DateTime? NextFetchQuotesTime {
        get => _nextFetchQuotesTime;
        set {
            _nextFetchQuotesTime = value;
            IsDirty = true;
        }
    }

    public InstrumentKey PrevInstrumentKey {
        get => _prevInstrumentKey;
        set {
            _prevInstrumentKey = value;
            IsDirty = true;
        }
    }

    public DateTime? GetNextRawDataTimeout() {
        DateTime? minDate = null;
        minDate = LesserDateAccountingForUndefined(minDate, _nextFetchDirectoryTime);
        minDate = LesserDateAccountingForUndefined(minDate, _nextFetchInstrumentDataTime);
        return minDate;
    }

    private static DateTime? LesserDateAccountingForUndefined(DateTime? dt1, DateTime? dt2) {
        if (dt1 is null && dt2 is null)
            return null;
        if (dt1 is null)
            return dt2;
        if (dt2 is null)
            return dt1;
        return dt1 < dt2 ? dt1 : dt2;
    }
}
