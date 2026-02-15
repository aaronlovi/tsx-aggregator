using System;
using System.Collections.Generic;
using tsx_aggregator.Services;
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

/// <summary>
/// "Raw" data report with information directly from the scraper.
/// </summary>
public record CurrentInstrumentRawDataReportDto(
    long InstrumentReportId,
    long InstrumentId,
    int ReportType,
    int ReportPeriodType,
    string ReportJson,
    DateOnly ReportDate);

public record ProcessedInstrumentReportDto(
    long InstrumentId,
    string SerializedReport,
    DateTimeOffset ReportCreatedDate,
    DateTimeOffset? ReportObsoletedDate);

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
    // List of company name exceptions that are not considered mutual funds despite naming conventions
    private static readonly List<string> TsxMutualFundExceptionsList = new() {
        "A&W Revenue Royalties Income"
        // Add more exceptions here as needed
    };

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

    public bool IsTsxPreferredShares {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            if (CompanySymbol.ContainsOrdinal(".PR."))
                return true;
            if (CompanySymbol.EndsWithOrdinal(".PR"))
                return true;
            if (CompanySymbol.ContainsOrdinal(".PF."))
                return true;
            if (CompanySymbol.EndsWithOrdinal(".PF"))
                return true;
            if (InstrumentSymbol.ContainsOrdinal(".PR."))
                return true;
            if (InstrumentSymbol.EndsWithOrdinal(".PR"))
                return true;
            if (InstrumentSymbol.ContainsOrdinal(".PF."))
                return true;
            if (InstrumentSymbol.EndsWithOrdinal(".PF"))
                return true;
            return false;
        }
    }

    public bool IsTsxWarrant {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            if (CompanySymbol.ContainsOrdinal(".WT."))
                return true;
            if (CompanySymbol.EndsWithOrdinal(".WT"))
                return true;
            if (InstrumentSymbol.ContainsOrdinal(".WT."))
                return true;
            if (InstrumentSymbol.EndsWithOrdinal(".WT"))
                return true;
            if (InstrumentSymbol.ContainsOrdinal(".WS."))
                return true;
            if (InstrumentSymbol.EndsWithOrdinal(".WS"))
                return true;
            return false;
        }
    }

    public bool IsTsxCompanyBonds {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            if (CompanySymbol.ContainsOrdinal(".DB."))
                return true;
            if (CompanySymbol.EndsWithOrdinal(".DB"))
                return true;
            if (InstrumentSymbol.ContainsOrdinal(".DB."))
                return true;
            if (InstrumentSymbol.EndsWithOrdinal(".DB"))
                return true;
            return false;
        }
    }

    public bool IsTsxETF {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            if (CompanyName.ContainsOrdinal(" ETF"))
                return true;
            if (InstrumentName.ContainsOrdinal(" ETF"))
                return true;
            return false;
        }
    }

    public bool IsTsxBmoMutualFund {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            return CompanyName.StartsWithOrdinal("BMO") && CompanyName.EndsWithOrdinal(" Fund");
        }
    }

    public bool IsTsxMutualFund {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;

            // Check if CompanyName matches any exception
            foreach (var exception in TsxMutualFundExceptionsList) {
                if (CompanyName.StartsWithOrdinal(exception))
                    return false;
            }

            if (CompanyName.ContainsOrdinal("Income Fund"))
                return true;

            if (CompanyName.ContainsOrdinal("Bitcoin Fund"))
                return true;

            if (CompanyName.StartsWithOrdinal("CI Global"))
                return true;

            if (CompanyName.StartsWithOrdinal("CIBC") && CompanyName.EndsWithOrdinal("Fixed Income Pool"))
                return true;

            if (CompanyName.StartsWithOrdinal("Dividend"))
                return true;

            if (CompanyName.ContainsOrdinal("Split Corp."))
                return true;

            if (CompanyName.StartsWithOrdinal("Guardian Directed") && CompanyName.EndsWithOrdinal("Portfolio"))
                return true;

            if (CompanyName.ContainsOrdinal("Global Bond"))
                return true;

            if (CompanyName.EndsWithOrdinal("ActivETF"))
                return true;

            return CompanyName.EndsWithOrdinal(" Fund");
        }
    }

    public bool IsTsxCdr {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            return CompanyName.ContainsOrdinal("CDR (CAD Hedged)");
        }
    }

    public bool IsTsxPrivatePool {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            return CompanyName.ContainsOrdinal("Private Pool");
        }
    }

    public bool IsTsxFidelityPortfolio {
        get {
            if (!Exchange.EqualsOrdinal(Constants.TsxExchange))
                return false;
            return CompanyName.StartsWithOrdinal("Fidelity") && CompanyName.EndsWithOrdinal("Portfolio");
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

public record InstrumentInfoDto(
    long InstrumentId,
    string Exchange,
    string CompanySymbol,
    string InstrumentSymbol,
    string CompanyName,
    string InstrumentName);

public record PagedInstrumentInfoDto(
    int PageNumber,
    int PageSize,
    int TotalInstruments,
    IList<InstrumentInfoDto> Instruments) {
    public static PagedInstrumentInfoDto WithPageNumberAndSizeOnly(int pageNumber, int pageSize)
        => new(pageNumber, pageSize, 0, Array.Empty<InstrumentInfoDto>());

    public bool IsValid => PageNumber > 0 && PageSize > 0 && TotalInstruments > 0 && Instruments.Count > 0;

    public GetInstrumentsWithNoRawReportsReply ToGetInstrumentsWithNoRawReportsReply() {
        var retVal = new GetInstrumentsWithNoRawReportsReply() {
            Success = true,
            TotalItems = TotalInstruments,
            PageNumber = PageNumber,
            PageSize = PageSize,
        };
        foreach (var dto in Instruments) {
            retVal.Instruments.Add(new InstrumentInfoItem {
                InstrumentId = (ulong)dto.InstrumentId,
                Exchange = dto.Exchange,
                CompanySymbol = dto.CompanySymbol,
                InstrumentSymbol = dto.InstrumentSymbol,
                CompanyName = dto.CompanyName,
                InstrumentName = dto.InstrumentName,
            });
        }
        return retVal;
    }
}

public record InstrumentRawDataReportDto(
    long InstrumentReportId,
    long InstrumentId,
    int ReportType,
    int ReportPeriodType,
    string ReportJson,
    DateOnly ReportDate,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ObsoletedDate,
    bool IsCurrent);

public class ApplicationCommonState {
    private bool _isPaused;
    private DateTime? _nextFetchDirectoryTime;
    private DateTime? _nextFetchInstrumentDataTime;
    private DateTime? _nextFetchQuotesTime;
    private InstrumentKey _prevInstrumentKey;

    public ApplicationCommonState() : this(false, null, null, null, InstrumentKey.Empty) { }

    public ApplicationCommonState(
        bool isPaused,
        DateTime? nextFetchDirectoryTime,
        DateTime? nextFetchInstrumentDataTime,
        DateTime? nextFetchQuotesTime,
        InstrumentKey prevInstrumentKey) {
        _isPaused = isPaused;
        _nextFetchDirectoryTime = nextFetchDirectoryTime;
        _nextFetchInstrumentDataTime = nextFetchInstrumentDataTime;
        _nextFetchQuotesTime = nextFetchQuotesTime;
        _prevInstrumentKey = prevInstrumentKey;
    }

    public ApplicationCommonState(ApplicationCommonState other)
        : this(other._isPaused, other.NextFetchDirectoryTime, other.NextFetchInstrumentDataTime, other.NextFetchQuotesTime, other.PrevInstrumentKey) {
    }

    public bool IsDirty { get; private set; }

    public bool IsPaused {
        get => _isPaused;
        set {
            // For IsPaused, do not set IsDirty if the value is the same
            if (_isPaused == value)
                return;

            _isPaused = value;
            IsDirty = true;
        }
    }

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
