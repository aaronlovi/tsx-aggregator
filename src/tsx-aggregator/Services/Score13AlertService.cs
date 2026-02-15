using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

internal class Score13AlertService : BackgroundService {
    private long _reqId;
    private readonly IStocksDataRequestsProcessor _requestProcessor;
    private readonly IQuoteService _quotesService;
    private readonly IEmailService _emailService;
    private readonly AlertSettingsOptions _settings;
    private readonly ILogger _logger;
    private IReadOnlyList<Score13Company>? _previousList;

    public Score13AlertService(
        IStocksDataRequestsProcessor requestProcessor,
        IQuoteService quotesService,
        IEmailService emailService,
        IOptions<AlertSettingsOptions> options,
        ILogger<Score13AlertService> logger) {

        _requestProcessor = requestProcessor;
        _quotesService = quotesService;
        _emailService = emailService;
        _settings = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("Score13AlertService - Starting");

        if (!ValidateSettings()) {
            _logger.LogError("Score13AlertService - Invalid AlertSettings configuration, exiting");
            return;
        }

        try {
            _logger.LogInformation("Score13AlertService - Waiting for QuoteService to be ready");
            await _quotesService.QuoteServiceReady.Task.WaitAsync(stoppingToken);
            _logger.LogInformation("Score13AlertService - QuoteService is ready");

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await CheckAndAlertAsync(stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    _logger.LogError(ex, "Score13AlertService - Error during check cycle");
                }

                await Task.Delay(TimeSpan.FromMinutes(_settings.CheckIntervalMinutes), stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Normal shutdown
        }

        _logger.LogInformation("Score13AlertService - Stopped");
    }

    private async Task CheckAndAlertAsync(CancellationToken ct) {
        long reqId = Interlocked.Increment(ref _reqId);

        // Step 1: Get stock data from DB
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var req = new GetStocksForExchangeRequest(reqId, "TSX", cts);
        if (!_requestProcessor.PostRequest(req)) {
            _logger.LogWarning("Score13AlertService - Failed to post request to request processor");
            return;
        }

        object? response = await req.Completed.Task;
        if (response is not GetStocksDataReply reply || !reply.Success) {
            _logger.LogWarning("Score13AlertService - Received invalid response from request processor");
            return;
        }

        // Step 2: Fill prices from QuoteService
        var symbols = new List<string>();
        foreach (GetStocksDataReplyItem replyItem in reply.StocksData) {
            symbols.Add(replyItem.InstrumentSymbol);
        }

        using CancellationTokenSource cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var req2 = new QuoteServiceFillPricesForSymbolsInput(reqId, cts2, symbols);
        if (!_quotesService.PostRequest(req2)) {
            _logger.LogWarning("Score13AlertService - Failed to post request to quotes service");
            return;
        }

        object? response2 = await req2.Completed.Task;
        if (response2 is not IDictionary<string, decimal> prices) {
            _logger.LogWarning("Score13AlertService - Received invalid response from quotes service");
            return;
        }

        // Step 3: Remove items missing prices
        int numItemsMissingPrices = 0;
        for (int i = reply.StocksData.Count - 1; i >= 0; i--) {
            GetStocksDataReplyItem replyItem = reply.StocksData[i];
            if (prices.TryGetValue(replyItem.InstrumentSymbol, out decimal price)) {
                replyItem.PerSharePrice = price;
            } else {
                reply.StocksData.RemoveAt(i);
                numItemsMissingPrices++;
            }
        }

        _logger.LogInformation("Score13AlertService - Fetched {Count} companies, {Missing} missing prices",
            reply.StocksData.Count, numItemsMissingPrices);

        // Step 4: Construct CompanyFullDetailReport objects
        var reports = new List<CompanyFullDetailReport>();
        foreach (GetStocksDataReplyItem item in reply.StocksData) {
            reports.Add(new CompanyFullDetailReport(
                exchange: item.Exchange,
                companySymbol: item.CompanySymbol,
                instrumentSymbol: item.InstrumentSymbol,
                companyName: item.CompanyName,
                instrumentName: item.InstrumentName,
                pricePerShare: item.PerSharePrice,
                curLongTermDebt: item.CurrentLongTermDebt,
                curTotalShareholdersEquity: item.CurrentTotalShareholdersEquity,
                curBookValue: item.CurrentBookValue,
                curNumShares: item.CurrentNumShares,
                averageNetCashFlow: item.AverageNetCashFlow,
                averageOwnerEarnings: item.AverageOwnerEarnings,
                curDividendsPaid: item.CurrentDividendsPaid,
                curAdjustedRetainedEarnings: item.CurrentAdjustedRetainedEarnings,
                oldestRetainedEarnings: item.OldestRetainedEarnings,
                numAnnualProcessedCashFlowReports: item.NumAnnualProcessedCashFlowReports));
        }

        // Step 5: Compute score-13 list
        IReadOnlyList<Score13Company> currentList = Score13DiffComputer.ComputeScore13List(reports);

        _logger.LogInformation("Score13AlertService - Found {Count} score-13 companies", currentList.Count);

        // Step 6: First run establishes baseline
        if (_previousList is null) {
            _previousList = currentList;
            _logger.LogInformation("Score13AlertService - Baseline established with {Count} score-13 companies", currentList.Count);
            return;
        }

        // Step 7: Compute diff
        Score13Diff? diff = Score13DiffComputer.ComputeDiff(_previousList, currentList);
        if (diff is null) {
            _logger.LogInformation("Score13AlertService - No change in score-13 list");
            _previousList = currentList;
            return;
        }

        // Step 8: Send email alert
        string subject = Score13DiffComputer.FormatAlertSubject(diff);
        string body = Score13DiffComputer.FormatAlertBody(diff);

        _logger.LogInformation("Score13AlertService - Score-13 list changed: {Added} added, {Removed} removed. Sending alert.",
            diff.Added.Count, diff.Removed.Count);

        _ = await _emailService.SendEmailAsync(subject, body, ct);

        // Update previous list regardless of email success to avoid repeat alerts
        _previousList = currentList;
    }

    private bool ValidateSettings() {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost)) {
            _logger.LogError("Score13AlertService - SmtpHost is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SmtpUsername)) {
            _logger.LogError("Score13AlertService - SmtpUsername is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SmtpPassword)) {
            _logger.LogError("Score13AlertService - SmtpPassword is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail)) {
            _logger.LogError("Score13AlertService - SenderEmail is not configured");
            return false;
        }
        if (_settings.Recipients is null || _settings.Recipients.Length == 0) {
            _logger.LogError("Score13AlertService - No recipients configured");
            return false;
        }
        return true;
    }
}
