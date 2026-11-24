using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>
/// Responsible for processing data requests one-by-one
/// </summary>
/// <remarks>
/// TODO: Add checks for overloading the service
/// </remarks>
public class StocksDataRequestsProcessor : BackgroundService, IStocksDataRequestsProcessor {
    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Channel<StocksDataRequestsInputBase> _inputChannel;
    private readonly ScoringOptions _scoring;

    public StocksDataRequestsProcessor(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StocksDataRequestsProcessor>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<StocksDataRequestsInputBase>();
        _scoring = svp.GetService<IOptions<ScoringOptions>>()?.Value ?? new ScoringOptions();

        _logger.LogInformation("StocksDataRequestsProcessor - Created");
    }

    public bool PostRequest(StocksDataRequestsInputBase inp) => _inputChannel.Writer.TryWrite(inp);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = Heartbeat.RunAsync(() => _logger.LogInformation("StocksDataFetchingService heartbeat"), stoppingToken); // Fire and forget

        _logger.LogInformation("StocksDataRequestsProcessor - Entering main loop");

        await foreach (StocksDataRequestsInputBase inputBase in _inputChannel.Reader.ReadAllAsync(stoppingToken)) {

            _logger.LogInformation("StocksDataRequestsProcessor - Got a message {Input}", inputBase);

            using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = inputBase.ReqId });
            using var thisRequestCts = CancellationTokenSource.CreateLinkedTokenSource(inputBase.CancellationToken, stoppingToken);

            // An exception that escaped ExecuteAsync would stop the whole host, so each request is contained here.
            try {
                switch (inputBase) {
                    case GetStocksForExchangeRequest getStocksForExchangeRequest: {
                        await ProcessGetStocksForExchangeRequest(getStocksForExchangeRequest, thisRequestCts.Token);
                        break;
                    }
                    case GetStockDetailsForExchangeRequest getStockDetailsForExchangeRequest: {
                        await ProcessGetStockDetailsRequest(getStockDetailsForExchangeRequest, thisRequestCts.Token);
                        break;
                    }
                    case GetDashboardStatsRequestInput getDashboardStatsRequest: {
                        await ProcessGetDashboardStatsRequest(getDashboardStatsRequest, thisRequestCts.Token);
                        break;
                    }
                    default: {
                        _logger.LogError("StocksDataFetch main loop - Invalid request type received, dropping input");
                        _ = inputBase.Completed.TrySetException(new InvalidOperationException("Invalid request type"));
                        break;
                    }
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                _ = inputBase.Completed.TrySetCanceled(stoppingToken);
                break;
            } catch (OperationCanceledException) {
                _logger.LogInformation("StocksDataRequestsProcessor - Request {ReqId} was cancelled by its caller", inputBase.ReqId);
                _ = inputBase.Completed.TrySetCanceled();
            } catch (Exception ex) {
                _logger.LogError(ex, "StocksDataRequestsProcessor - Request {ReqId} failed", inputBase.ReqId);
                _ = inputBase.Completed.TrySetException(ex);
            }
        }

        _logger.LogInformation("StocksDataRequestsProcessor - Exiting main loop");
    }

    private async Task ProcessGetStocksForExchangeRequest(GetStocksForExchangeRequest request, CancellationToken ct) {
        using var exchangeContext = _logger.BeginScope(new Dictionary<string, string> { [LogUtils.ExchangeContext] = request.Exchange });
        _logger.LogInformation("ProcessGetStocksForExchangeRequest");

        Result<IReadOnlyList<ProcessedFullInstrumentReportDto>> res = await _dbm.GetProcessedStockDataByExchange(request.Exchange, ct);

        if (res.Success) {
            _logger.LogInformation("ProcessGetStocksForExchangeRequest Success - {NumItems}", res.Data!.Count);
        } else {
            _logger.LogInformation("ProcessGetStocksForExchangeRequest Failed - {Error}", res.ErrMsg);
        }

        var reply = new GetStocksDataReply() {
            Success = res.Success,
            ErrorMessage = res.ErrMsg
        };
        foreach (ProcessedFullInstrumentReportDto dto in res.Data ?? []) {
            using var companyContext = _logger.BeginScope(new Dictionary<string, string> { [LogUtils.InstrumentSymbolContext] = dto.InstrumentSymbol });
            if (TryBuildReplyItem("ProcessGetStocksForExchangeRequest", request.Exchange, dto) is GetStocksDataReplyItem item)
                reply.StocksData.Add(item);
        }

        _ = request.Completed.TrySetResult(reply);
    }

    private async Task ProcessGetStockDetailsRequest(GetStockDetailsForExchangeRequest request, CancellationToken ct) {
        using var logContext = _logger.BeginScope(new Dictionary<string, string> {
            [LogUtils.ExchangeContext] = request.Exchange,
            [LogUtils.InstrumentSymbolContext] = request.InstrumentSymbol
        });
        _logger.LogInformation("ProcessGetStockDetailsRequest");

        Result<ProcessedFullInstrumentReportDto> res = await _dbm.GetProcessedStockDataByExchangeAndSymbol(request.Exchange, request.InstrumentSymbol, ct);

        if (!res.Success) {
            _logger.LogWarning("ProcessGetStockDetailsRequest Failed - {Error}", res.ErrMsg);
        } else if (res.Data is null) {
            // The query found no row: a not-found, not a DB failure (whose own error is kept above)
            res = res with { Success = false, ErrMsg = "No data found" };
            _logger.LogInformation("ProcessGetStockDetailsRequest - No data found");
        } else {
            _logger.LogInformation("ProcessGetStockDetailsRequest Success");
        }

        var reply = new GetStocksDetailReply() {
            Success = res.Success,
            ErrorMessage = res.ErrMsg
        };
        if (res.Data is not null) {
            reply.StockDetail = TryBuildReplyItem("ProcessGetStockDetailsRequest", request.Exchange, res.Data);
        }

        _ = request.Completed.TrySetResult(reply);
    }

    /// <summary>
    /// Parses a company's processed report and builds its reply item. A report that does not parse is logged
    /// (prefixed with <paramref name="requestName"/>) and gives null.
    /// </summary>
    private GetStocksDataReplyItem? TryBuildReplyItem(string requestName, string exchange, ProcessedFullInstrumentReportDto dto) {
        try {
            if (!ProcessedReportFields.TryParse(dto.SerializedReport, out ProcessedReportFields? fields)) {
                _logger.LogWarning("{RequestName} company {CompanySymbol},{InstrumentSymbol} not processed - Invalid JSON {SerializedReport}",
                    requestName, dto.CompanySymbol, dto.InstrumentSymbol, dto.SerializedReport);
                return null;
            }

            return BuildReplyItem(exchange, dto, fields, _scoring);
        } catch (Exception ex) {
            _logger.LogError(ex, "{RequestName} company {CompanySymbol},{InstrumentSymbol},{ReportJson} not processed - Exception",
                requestName, dto.CompanySymbol, dto.InstrumentSymbol, dto.SerializedReport);
            return null;
        }
    }

    private static GetStocksDataReplyItem BuildReplyItem(string exchange, ProcessedFullInstrumentReportDto dto, ProcessedReportFields fields, ScoringOptions scoring) {
        var item = new GetStocksDataReplyItem() {
            Exchange = exchange,
            CompanySymbol = dto.CompanySymbol,
            InstrumentSymbol = dto.InstrumentSymbol,
            CompanyName = dto.CompanyName,
            InstrumentName = dto.InstrumentName,
            CreatedDate = Timestamp.FromDateTimeOffset(dto.ReportCreatedDate),
            CurrentNumShares = fields.CurNumShares,
            CurrentGoodwill = fields.CurGoodwill,
            CurrentIntangibles = fields.CurIntangibles,
            CurrentLongTermDebt = fields.CurLongTermDebt,
            CurrentShortTermDebt = fields.CurShortTermDebt,
            CurrentRetainedEarningsEstimated = fields.CurRetainedEarningsEstimated,
            OldestRetainedEarningsEstimated = fields.OldestRetainedEarningsEstimated,
            PerSharePrice = 0M, // To be filled out later by the quotes service
            NumAnnualProcessedCashFlowReports = dto.NumAnnualCashFlowReports,
            RoeConsistencyMinYears = scoring.RoeConsistencyMinYears,
            RoeConsistencyWindowYears = scoring.RoeConsistencyWindowYears,
            RoeConsistencyMinUsableYears = scoring.RoeConsistencyMinUsableYears,
            MostRecentAnnualReportDate = dto.MostRecentAnnualReportDate.HasValue
                ? Timestamp.FromDateTimeOffset(dto.MostRecentAnnualReportDate.Value) : null,
            MostRecentQuarterlyReportDate = dto.MostRecentQuarterlyReportDate.HasValue
                ? Timestamp.FromDateTimeOffset(dto.MostRecentQuarterlyReportDate.Value) : null,
            LastUpdatedDate = dto.LastUpdatedDate.HasValue
                ? Timestamp.FromDateTimeOffset(dto.LastUpdatedDate.Value) : null
        };

        // Optional fields stay unset when the source data didn't have the figure (W1).
        if (fields.CurTotalShareholdersEquity is long equity)
            item.CurrentTotalShareholdersEquity = equity;
        if (fields.CurDividendsPaid is long dividends)
            item.CurrentDividendsPaid = dividends;
        if (fields.AverageDividendsPaid is long averageDividends)
            item.AverageDividendsPaid = averageDividends;
        if (fields.CurAdjustedRetainedEarnings is long adjustedRetainedEarnings)
            item.CurrentAdjustedRetainedEarnings = adjustedRetainedEarnings;
        if (fields.OldestRetainedEarnings is long oldestRetainedEarnings)
            item.OldestRetainedEarnings = oldestRetainedEarnings;
        if (fields.CurBookValue is long bookValue)
            item.CurrentBookValue = bookValue;
        if (fields.AverageNetCashFlow is long netCashFlow)
            item.AverageNetCashFlow = netCashFlow;
        if (fields.AverageOwnerEarnings is long ownerEarnings)
            item.AverageOwnerEarnings = ownerEarnings;
        if (fields.LatestOwnerEarnings is long latestOwnerEarnings)
            item.LatestOwnerEarnings = latestOwnerEarnings;
        if (fields.RoeCashFlowNumerator is long roeCashFlowNumerator)
            item.RoeCashFlowNumerator = roeCashFlowNumerator;
        if (fields.RoeCashFlowEquity is long roeCashFlowEquity)
            item.RoeCashFlowEquity = roeCashFlowEquity;
        if (fields.RoeOwnerEarningsNumerator is long roeOwnerEarningsNumerator)
            item.RoeOwnerEarningsNumerator = roeOwnerEarningsNumerator;
        if (fields.RoeOwnerEarningsEquity is long roeOwnerEarningsEquity)
            item.RoeOwnerEarningsEquity = roeOwnerEarningsEquity;
        foreach (AnnualReturnOnEquity year in fields.RoeOwnerEarningsByYear ?? []) {
            var annualRoe = new AnnualRoe { YearEnd = year.YearEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
            if (year.Roe is decimal roe)
                annualRoe.Roe = roe;
            item.RoeOwnerEarningsByYear.Add(annualRoe);
        }
        return item;
    }

    private async Task ProcessGetDashboardStatsRequest(GetDashboardStatsRequestInput request, CancellationToken ct) {
        _logger.LogInformation("ProcessGetDashboardStatsRequest");

        (Result res, models.DashboardStatsDto? dto) = await _dbm.GetDashboardStats(ct);

        var reply = new GetDashboardStatsReply() {
            Success = res.Success,
            ErrorMessage = res.ErrMsg
        };

        if (res.Success && dto is not null) {
            reply.TotalActiveInstruments = dto.TotalActiveInstruments;
            reply.TotalObsoletedInstruments = dto.TotalObsoletedInstruments;
            reply.InstrumentsWithProcessedReports = dto.InstrumentsWithProcessedReports;
            reply.InstrumentsWithoutProcessedReports = dto.InstrumentsWithoutProcessedReports;
            reply.MostRecentRawIngestion = dto.MostRecentRawIngestion.HasValue
                ? Timestamp.FromDateTimeOffset(dto.MostRecentRawIngestion.Value) : null;
            reply.MostRecentAggregation = dto.MostRecentAggregation.HasValue
                ? Timestamp.FromDateTimeOffset(dto.MostRecentAggregation.Value) : null;
            reply.UnprocessedEventCount = dto.UnprocessedEventCount;

            foreach (models.RawReportCountByTypeDto countDto in dto.RawReportCountsByType) {
                reply.RawReportCounts.Add(new DashboardRawReportCount {
                    ReportType = countDto.ReportType,
                    Count = countDto.Count
                });
            }

            (Result stateRes, models.ApplicationCommonState? appState) = await _dbm.GetApplicationCommonState(ct);
            if (stateRes.Success && appState is not null) {
                reply.NextFetchDirectoryTime = appState.NextFetchDirectoryTime.HasValue
                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(appState.NextFetchDirectoryTime.Value, DateTimeKind.Utc)) : null;
                reply.NextFetchInstrumentDataTime = appState.NextFetchInstrumentDataTime.HasValue
                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(appState.NextFetchInstrumentDataTime.Value, DateTimeKind.Utc)) : null;
                reply.NextFetchQuotesTime = appState.NextFetchQuotesTime.HasValue
                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(appState.NextFetchQuotesTime.Value, DateTimeKind.Utc)) : null;
            }

            var aggregator = _svp.GetService<Aggregator>();
            if (aggregator?.NextCycleTime is { } nextCycleTime)
                reply.NextAggregatorCycleTime = Timestamp.FromDateTime(DateTime.SpecifyKind(nextCycleTime, DateTimeKind.Utc));

            _logger.LogInformation("ProcessGetDashboardStatsRequest Success - {ActiveInstruments} active instruments", dto.TotalActiveInstruments);
        } else {
            _logger.LogWarning("ProcessGetDashboardStatsRequest Failed - {Error}", res.ErrMsg);
        }

        _ = request.Completed.TrySetResult(reply);
    }
}
