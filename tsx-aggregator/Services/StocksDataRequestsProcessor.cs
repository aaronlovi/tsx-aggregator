using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using dbm_persistence;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private static readonly TimeSpan OneMinute = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Channel<StocksDataRequestsInputBase> _inputChannel;

    public StocksDataRequestsProcessor(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<StocksDataRequestsProcessor>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<StocksDataRequestsInputBase>();

        _logger.LogInformation("StocksDataRequestsProcessor - Created");
    }

    public bool PostRequest(StocksDataRequestsInputBase inp) {
        return _inputChannel.Writer.TryWrite(inp);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        _logger.LogInformation("StocksDataRequestsProcessor - Entering main loop");

        await foreach (StocksDataRequestsInputBase inputBase in _inputChannel.Reader.ReadAllAsync(stoppingToken)) {

            _logger.LogInformation("StocksDataRequestsProcessor - Got a message {Input}", inputBase);

            using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = inputBase.ReqId });
            using var thisRequestCts = Utilities.CreateLinkedTokenSource(inputBase.CancellationTokenSource, stoppingToken);

            switch (inputBase) {
                case GetStocksForExchangeRequest getStocksForExchangeRequest: {
                    await ProcessGetStocksForExchangeRequest(getStocksForExchangeRequest, thisRequestCts.Token);
                    break;
                }
                default: {
                    _logger.LogError("StocksDataFetch main loop - Invalid request type received, dropping input");
                    break;
                }
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
        foreach (ProcessedFullInstrumentReportDto dto in res.Data ?? Array.Empty<ProcessedFullInstrumentReportDto>()) {
            using var companyContext = _logger.BeginScope(new Dictionary<string, string> { [LogUtils.InstrumentSymbolContext] = dto.InstrumentSymbol });
            AddCompanyItem(request.Exchange, dto, reply, _logger);
        }

        request.Completed.TrySetResult(reply);

        // Local helper methods

        static void AddCompanyItem(string exchange, ProcessedFullInstrumentReportDto dto, GetStocksDataReply getStocksDataReply, ILogger logger) {
            try {
                using var doc = JsonDocument.Parse(dto.SerializedReport);
                JsonElement root = doc.RootElement;
                if (!GetDataPoint(root, "CurNumShares", out var curNumShares)
                    || !GetDataPoint(root, "CurTotalShareholdersEquity", out var curTotalShareholdersEquity)
                    || !GetDataPoint(root, "CurGoodwill", out var curGoodWill)
                    || !GetDataPoint(root, "CurIntangibles", out var curIntangibles)
                    || !GetDataPoint(root, "CurLongTermDebt", out var curLongTermDebt)
                    || !GetDataPoint(root, "CurDividendsPaid", out var curDividendsPaid)
                    || !GetDataPoint(root, "CurRetainedEarnings", out var curRetainedEarnings)
                    || !GetDataPoint(root, "OldestRetainedEarnings", out var oldestRetainedEarnings)
                    || !GetDataPoint(root, "CurBookValue", out var currentBookValue)
                    || !GetDataPoint(root, "AverageNetCashFlow", out var averageNetCashFlow)
                    || !GetDataPoint(root, "AverageOwnerEarnings", out var averageOwnerEarnings)) {
                    logger.LogWarning("ProcessGetStocksForExchangeRequest company {CompanySymbol},{InstrumentSymbol} not processed - Invalid JSON {SerializedReport}",
                        dto.CompanySymbol, dto.InstrumentSymbol, dto.SerializedReport);
                    return;
                }

                var item = new GetStocksDataReplyItem() {
                    Exchange = exchange,
                    CompanySymbol = dto.CompanySymbol,
                    InstrumentSymbol = dto.InstrumentSymbol,
                    CompanyName = dto.CompanyName,
                    InstrumentName = dto.InstrumentName,
                    CreatedDate = Timestamp.FromDateTimeOffset(dto.ReportCreatedDate),
                    CurrentNumShares = curNumShares,
                    CurrentTotalShareholdersEquity = curTotalShareholdersEquity,
                    CurrentGoodwill = curGoodWill,
                    CurrentIntangibles = curIntangibles,
                    CurrentLongTermDebt = curLongTermDebt,
                    CurrentDividendsPaid = curDividendsPaid,
                    CurrentRetainedEarnings = curRetainedEarnings,
                    OldestRetainedEarnings = oldestRetainedEarnings,
                    CurrentBookValue = currentBookValue,
                    AverageNetCashFlow = averageNetCashFlow,
                    AverageOwnerEarnings = averageOwnerEarnings,
                    PerSharePrice = 0M, // To be filled out later by the quotes service
                    NumAnnualProcessedCashFlowReports = dto.NumAnnualCashFlowReports
                };
                getStocksDataReply.StocksData.Add(item);
            }
            catch (Exception ex) {
                logger.LogError(ex, "ProcessGetStocksForExchangeRequest company {CompanySymbol},{InstrumentSymbol},{ReportJson} not processed - Exception",
                    dto.CompanySymbol, dto.InstrumentSymbol, dto.SerializedReport);
            }
        }

        static bool GetDataPoint(JsonElement root, string fieldName, out long retVal) {
            if (root.TryGetProperty(fieldName, out JsonElement jsonVal) && jsonVal.ValueKind == JsonValueKind.Number) {
                retVal = (long)Math.Round(jsonVal.GetDecimal(), 0, MidpointRounding.ToEven);
                return true;
            } else {
                retVal = 0;
                return false;
            }
        }
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<StocksDataRequestsProcessor>>();
        while (!ct.IsCancellationRequested) {
            logger.LogInformation("StocksDataFetchingService heartbeat");
            await Task.Delay(OneMinute, ct);
        }
    }
}
