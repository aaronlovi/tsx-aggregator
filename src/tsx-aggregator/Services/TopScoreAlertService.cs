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

internal class TopScoreAlertService : BackgroundService {
    private long _reqId;
    private readonly IStocksDataRequestsProcessor _requestProcessor;
    private readonly IQuoteService _quotesService;
    private readonly IEmailService _emailService;
    private readonly AlertSettingsOptions _settings;
    private readonly ILogger _logger;
    private IReadOnlyList<TopScoreCompany>? _previousList;

    public TopScoreAlertService(
        IStocksDataRequestsProcessor requestProcessor,
        IQuoteService quotesService,
        IEmailService emailService,
        IOptions<AlertSettingsOptions> options,
        ILogger<TopScoreAlertService> logger) {

        _requestProcessor = requestProcessor;
        _quotesService = quotesService;
        _emailService = emailService;
        _settings = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("TopScoreAlertService - Starting");

        if (!ValidateSettings()) {
            _logger.LogError("TopScoreAlertService - Invalid AlertSettings configuration, exiting");
            return;
        }

        try {
            _logger.LogInformation("TopScoreAlertService - Waiting for QuoteService to be ready");
            await _quotesService.QuoteServiceReady.Task.WaitAsync(stoppingToken);
            _logger.LogInformation("TopScoreAlertService - QuoteService is ready");

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await CheckAndAlertAsync(stoppingToken);
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    _logger.LogError(ex, "TopScoreAlertService - Error during check cycle");
                }

                await Task.Delay(TimeSpan.FromMinutes(_settings.CheckIntervalMinutes), stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Normal shutdown
        }

        _logger.LogInformation("TopScoreAlertService - Stopped");
    }

    private async Task CheckAndAlertAsync(CancellationToken ct) {
        long reqId = Interlocked.Increment(ref _reqId);

        // Step 1: Get stock data from DB
        (bool posted, object? response) = await ServiceRequests.PostAndAwaitAsync(
            cts => new GetStocksForExchangeRequest(reqId, "TSX", cts), _requestProcessor.PostRequest, ct);
        if (!posted) {
            _logger.LogWarning("TopScoreAlertService - Failed to post request to request processor");
            return;
        }
        if (response is not GetStocksDataReply reply || !reply.Success) {
            _logger.LogWarning("TopScoreAlertService - Received invalid response from request processor");
            return;
        }

        // Step 2: Fill prices from QuoteService
        List<string> symbols = StockPrices.InstrumentSymbols(reply);
        (posted, response) = await ServiceRequests.PostAndAwaitAsync(
            cts => new QuoteServiceFillPricesForSymbolsInput(reqId, cts, symbols), _quotesService.PostRequest, ct);
        if (!posted) {
            _logger.LogWarning("TopScoreAlertService - Failed to post request to quotes service");
            return;
        }
        if (response is not IDictionary<string, decimal> prices) {
            _logger.LogWarning("TopScoreAlertService - Received invalid response from quotes service");
            return;
        }

        // Step 3: Remove items missing prices
        int numItemsMissingPrices = StockPrices.FillAndDropUnpriced(reply, prices);

        _logger.LogInformation("TopScoreAlertService - Fetched {Count} companies, {Missing} missing prices",
            reply.StocksData.Count, numItemsMissingPrices);

        // Step 4: Construct CompanyFullDetailReport objects
        var reports = new List<CompanyFullDetailReport>();
        foreach (GetStocksDataReplyItem item in reply.StocksData) {
            reports.Add(CompanyFullDetailReport.FromReplyItem(item));
        }

        // Step 5: Compute top-score list
        IReadOnlyList<TopScoreCompany> currentList = TopScoreDiffComputer.ComputeTopScoreList(reports, DateTimeOffset.UtcNow);

        _logger.LogInformation("TopScoreAlertService - Found {Count} top-score companies", currentList.Count);

        // Step 6: First run establishes baseline (or sends test email if configured)
        if (_previousList is null) {
            if (_settings.SendTestEmailOnStartup && currentList.Count > 0) {
                _logger.LogInformation("TopScoreAlertService - SendTestEmailOnStartup enabled, sending initial list as test email");
                var testDiff = TopScoreDiffComputer.ComputeDiff([], currentList);
                if (testDiff is not null) {
                    string testSubject = TopScoreDiffComputer.FormatAlertSubject(testDiff);
                    string testPlainBody = TopScoreDiffComputer.FormatAlertBody(testDiff);
                    string testHtmlBody = TopScoreDiffComputer.FormatAlertBodyHtml(testDiff);
                    _ = await _emailService.SendEmailAsync(testSubject, testPlainBody, testHtmlBody, ct);
                }
            }
            _previousList = currentList;
            _logger.LogInformation("TopScoreAlertService - Baseline established with {Count} top-score companies", currentList.Count);
            return;
        }

        // Step 7: Compute diff
        TopScoreDiff? diff = TopScoreDiffComputer.ComputeDiff(_previousList, currentList);
        if (diff is null) {
            _logger.LogInformation("TopScoreAlertService - No change in top-score list");
            _previousList = currentList;
            return;
        }

        // Step 8: Send email alert
        string subject = TopScoreDiffComputer.FormatAlertSubject(diff);
        string plainBody = TopScoreDiffComputer.FormatAlertBody(diff);
        string htmlBody = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        _logger.LogInformation("TopScoreAlertService - Top-Score list changed: {Added} added, {Removed} removed. Sending alert.",
            diff.Added.Count, diff.Removed.Count);

        _ = await _emailService.SendEmailAsync(subject, plainBody, htmlBody, ct);

        // Update previous list regardless of email success to avoid repeat alerts
        _previousList = currentList;
    }

    private bool ValidateSettings() {
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost)) {
            _logger.LogError("TopScoreAlertService - SmtpHost is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SmtpUsername)) {
            _logger.LogError("TopScoreAlertService - SmtpUsername is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SmtpPassword)) {
            _logger.LogError("TopScoreAlertService - SmtpPassword is not configured");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_settings.SenderEmail)) {
            _logger.LogError("TopScoreAlertService - SenderEmail is not configured");
            return false;
        }
        if (_settings.RecipientAddresses.Count == 0) {
            _logger.LogError("TopScoreAlertService - No recipients configured");
            return false;
        }
        return true;
    }
}
