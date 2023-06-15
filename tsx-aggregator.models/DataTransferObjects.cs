using System;
using System.Collections.Generic;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public record InstrumentEventDto(
    long InstrumentId,
    DateTimeOffset EventDate,
    int EventType,
    bool IsProcessed,
    string InstrumentSymbol,
    string InstrumentName,
    string Exchange,
    decimal PricePerShare,
    long NumShares);

public record InstrumentReportDto(
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

public record InstrumentDto : CompanyAndInstrumentSymbol {
    public InstrumentDto(
        ulong instrumentId,
        string exchange,
        string companySymbol,
        string companyName,
        string instrumentSymbol,
        string instrumentName,
        DateTimeOffset createdDate,
        DateTimeOffset? obsoletedDate)
        : base(companySymbol, instrumentSymbol) {
        InstrumentId = instrumentId;
        Exchange = exchange;
        CompanyName = companyName;
        InstrumentName = instrumentName;
        CreatedDate = createdDate;
        ObsoletedDate = obsoletedDate;
    }

    public ulong InstrumentId { get; init; }
    public string Exchange { get; init; }
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

public record CompanyAndInstrumentSymbol(string CompanySymbol, string InstrumentSymbol) {
    public static readonly CompanyAndInstrumentSymbol Empty = new(string.Empty, string.Empty);
    public static readonly ComparerBySymbols_ ComparerBySymbols = new();

    public static int CompareBySymbols(CompanyAndInstrumentSymbol c1, CompanyAndInstrumentSymbol c2) {
        int res = c1.CompanySymbol.CompareTo(c2.CompanySymbol);
        if (res == 0)
            res = c1.InstrumentSymbol.CompareTo(c2.InstrumentSymbol);
        return res;
    }

    public class ComparerBySymbols_ : IComparer<CompanyAndInstrumentSymbol> {
        public int Compare(CompanyAndInstrumentSymbol? x, CompanyAndInstrumentSymbol? y) {
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

public class StateFsmState {
    private DateTime? _nextFetchDirectoryTime;
    private DateTime? _nextFetchInstrumentDataTime;
    private CompanyAndInstrumentSymbol _prevCompanyAndInstrumentSymbol;

    public StateFsmState() : this(null, null, CompanyAndInstrumentSymbol.Empty) { }

    public StateFsmState(
        DateTime? nextFetchDirectoryTime,
        DateTime? nextFetchInstrumentDataTime,
        CompanyAndInstrumentSymbol prevCompanyAndInstrumentSymbol) {
        _nextFetchDirectoryTime = nextFetchDirectoryTime;
        _nextFetchInstrumentDataTime = nextFetchInstrumentDataTime;
        _prevCompanyAndInstrumentSymbol = prevCompanyAndInstrumentSymbol;
    }

    public StateFsmState(StateFsmState other)
        : this(other.NextFetchDirectoryTime, other.NextFetchInstrumentDataTime, other.PrevCompanyAndInstrumentSymbol) {
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

    public CompanyAndInstrumentSymbol PrevCompanyAndInstrumentSymbol {
        get => _prevCompanyAndInstrumentSymbol;
        set {
            _prevCompanyAndInstrumentSymbol = value;
            IsDirty = true;
        }
    }

    public DateTime? GetNextTimeout() {
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
