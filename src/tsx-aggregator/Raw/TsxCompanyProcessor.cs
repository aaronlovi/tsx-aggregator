using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator;

/// <summary>
/// Processor for a single instrument.
/// Basically, creates a Puppeteer browser to scrape financial data from the TMX Money website.
/// Contains an asynchronous loop which is kept alive until all responses from the web page are complete.
/// </summary>
internal sealed class TsxCompanyProcessor : BackgroundService, IDisposable {
    private const int MaxRetries = 2;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;
    private readonly InstrumentDto _instrumentDto;
    private readonly TsxCompanyProcessorFsm _fsm;
    private int _currentAttempt;                  // owned by the ExecuteAsync loop
    private TaskCompletionSource<string?>? _attemptDone; // owned by the ExecuteAsync loop; null result = success
    private readonly Channel<TsxCompanyProcessorFsmInputBase> _inputChannel;
    private readonly CancellationToken _externalCancellationToken;
    private readonly ScraperOptions _scraperOptions;

    private TsxCompanyProcessor(
        InstrumentDto instrumentDto,
        IServiceProvider svp,
        CancellationToken externalCancellationToken) {
        _logger = svp.GetRequiredService<ILogger<TsxCompanyProcessor>>();
        _instrumentDto = instrumentDto;
        _fsm = new(_instrumentDto, svp.GetRequiredService<ILogger<TsxCompanyProcessorFsm>>());
        _inputChannel = Channel.CreateUnbounded<TsxCompanyProcessorFsmInputBase>();
        _externalCancellationToken = externalCancellationToken;
        _scraperOptions = svp.GetService<IOptions<ScraperOptions>>()?.Value ?? new ScraperOptions();
        ScrapeAttempt = ScrapeWithBrowser;
    }

    /// <summary>Wait before a retry.</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One scrape attempt: ends with null when the company's data is complete, or with an error message when a
    /// response cannot be used. May throw. The browser, unless a test replaces it.
    /// </summary>
    internal Func<int, Task<string?>> ScrapeAttempt { get; set; }

    public bool IsFaulted { get; private set; }
    public TsxCompanyData? CompanyReport { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _externalCancellationToken);
        CancellationToken ct = cts.Token;

        try {
            // Everything that touches the parser runs here, on this loop, so a retry cannot reset it mid-response.
            await foreach (TsxCompanyProcessorFsmInputBase input in _inputChannel.Reader.ReadAllAsync(ct)) {
                switch (input) {
                    case StartAttemptInput start:
                        _fsm.Reset();
                        _currentAttempt = start.Attempt;
                        _attemptDone = start.Done;
                        break;
                    case GotResponse response when response.Attempt != _currentAttempt:
                        _logger.LogInformation("Ignoring a late response from attempt {Attempt} for {Instrument}", response.Attempt, _instrumentDto);
                        break;
                    default:
                        ProcessOutputs(_fsm.Update(input));
                        break;
                }
            }
        } catch (OperationCanceledException) {
            _logger.LogWarning("TsxCompanyProcessor canceled. {Instrument}", _instrumentDto);
        } catch (Exception ex) {
            _logger.LogError(ex, "TsxCompanyProcessor general fault. {Instrument}", _instrumentDto);
            IsFaulted = true;
        }
    }

    public static async Task<TsxCompanyProcessor> Create(InstrumentDto instrumentDto, IServiceProvider svp, CancellationToken externalCancellationToken) {
        var processor = new TsxCompanyProcessor(instrumentDto, svp, externalCancellationToken);
        if (!processor._scraperOptions.UsesInstalledBrowser)
            await DownloadBrowser();
        return processor;
    }

    /// <summary>
    /// Headless Chrome: the installed one with its extra arguments when Scraper:BrowserExecutablePath is set,
    /// otherwise the one Puppeteer downloaded.
    /// </summary>
    internal static LaunchOptions BuildLaunchOptions(ScraperOptions options) => new() {
        Headless = true,
        ExecutablePath = options.UsesInstalledBrowser ? options.BrowserExecutablePath : null,
        Args = options.BrowserArgs ?? [],
    };

    public async Task<Result<TsxCompanyData>> GetRawFinancials() {
        int attempt = 0;
        Result<TsxCompanyData> result;

        do {
            attempt++;
            if (attempt > 1) {
                _logger.LogInformation("GetRawFinancials retry attempt {Attempt} of {MaxRetries} for instrument {Instrument}",
                    attempt - 1, MaxRetries, _instrumentDto);
                
                try {
                    await Task.Delay(RetryDelay, _externalCancellationToken);
                } catch (OperationCanceledException) {
                    _logger.LogInformation("GetRawFinancials retry cancelled for instrument {Instrument}", _instrumentDto);
                    return Result<TsxCompanyData>.SetFailure("Retry cancelled");
                }
            }

            result = await GetRawFinancialsCore(attempt);

        } while (!result.Success && attempt <= MaxRetries && !_externalCancellationToken.IsCancellationRequested);

        // A failed try is expected now and then and is only a warning; the company getting no data is the error.
        // TMX having no annual reports for an instrument (funds, new listings) is not an error either: nothing is wrong.
        if (!result.Success && !_externalCancellationToken.IsCancellationRequested) {
            LogLevel level = result.ErrMsg.EndsWith(TsxCompanyProcessorFsm.NoAnnualReportsError, StringComparison.Ordinal)
                ? LogLevel.Warning
                : LogLevel.Error;
            _logger.Log(level, "GetRawFinancials failed after {Attempts} attempts for instrument {Instrument}: {Error}",
                attempt, _instrumentDto, result.ErrMsg);
        }

        return result;
    }

    /// <summary>
    /// Starts a scrape attempt: the parser is reset on the loop, and from then on only responses tagged with this
    /// attempt count. The task ends with null when the company's data is complete, or with an error message as soon
    /// as a response for this instrument cannot be used.
    /// </summary>
    internal Task<string?> BeginAttempt(int attempt) {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inputChannel.Writer.TryWrite(new StartAttemptInput(attempt, done)))
            done.TrySetResult("The processor is closed");
        return done.Task;
    }

    internal bool PostResponse(GotResponse response) => _inputChannel.Writer.TryWrite(response);

    private async Task<Result<TsxCompanyData>> GetRawFinancialsCore(int attempt) {
        CompanyReport = null;

        try {
            string? error = await ScrapeAttempt(attempt);

            if (error is not null) {
                _logger.LogInformation("GetRawFinancials could not use a response for instrument {Instrument}: {Error}", _instrumentDto, error);
                return Result<TsxCompanyData>.SetFailure("Unusable response: " + error);
            }
            return Result<TsxCompanyData>.SetSuccess(CompanyReport!);
        }
        catch (OperationCanceledException) when (_externalCancellationToken.IsCancellationRequested) {
            _logger.LogInformation("GetRawFinancials cancelled for instrument {Instrument}", _instrumentDto);
            return Result<TsxCompanyData>.SetFailure("Cancelled");
        }
        catch (OperationCanceledException) {
            _logger.LogInformation("GetRawFinancials timed out for instrument {Instrument}", _instrumentDto);
            return Result<TsxCompanyData>.SetFailure("Timed out");
        }
        catch (NavigationException ex) {
            // TMX did not answer within the navigation timeout, or the network failed: it happens, and a retry
            // usually gets the data, so no stack trace (R97)
            _logger.LogWarning("GetRawFinancials page load failed for instrument {Instrument}, attempt {Attempt}: {Error}",
                _instrumentDto, attempt, ex.Message);
            return Result<TsxCompanyData>.SetFailure("Page load failed: " + ex.Message);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "GetRawFinancials general fault for instrument {Instrument}", _instrumentDto);
            return Result<TsxCompanyData>.SetFailure("General fault: " + ex.Message);
        }
    }

    private async Task<string?> ScrapeWithBrowser(int attempt) {
        Task<string?> attemptDone = BeginAttempt(attempt);
        LaunchOptions launchOptions = BuildLaunchOptions(_scraperOptions);
        await using IBrowser browser = await Puppeteer.LaunchAsync(launchOptions);
        await using IPage page = await browser.NewPageAsync();
        page.DefaultNavigationTimeout = 60000;
        void OnResponse(object? sender, ResponseCreatedEventArgs e) => ProcessPageResponse(e, attempt);
        page.Response += OnResponse;

        // The attempt ends on success, on an unusable response, after the timeout, or at shutdown.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_externalCancellationToken);
        timeout.CancelAfter(AttemptTimeout);
        _ = await page.GoToAsync($"https://money.tmx.com/en/quote/{_instrumentDto.InstrumentSymbol}/financials-filings");

        string? error = await attemptDone.WaitAsync(timeout.Token);

        page.Response -= OnResponse;
        await browser.CloseAsync();
        return error;
    }

    private static async Task DownloadBrowser() {
        var browserFetcher = new BrowserFetcher();
        _ = await browserFetcher.DownloadAsync();
    }

    private async void ProcessPageResponse(ResponseCreatedEventArgs e, int attempt) {
        try {
            if (e.Response.Status != HttpStatusCode.OK)
                return;

            if (!e.Response.Url.Contains("getEnhancedQuotes.json")
                && !e.Response.Url.Contains("getFinancialsEnhancedBySymbol.json"))
                return;

            var headers = e.Response.Headers;
            if (!headers.ContainsKey("content-encoding"))
                return;

            try {
                string responseText = await e.Response.TextAsync();
                _ = _inputChannel.Writer.TryWrite(new GotResponse() {
                    Attempt = attempt,
                    Text = responseText,
                    Url = e.Response.Url
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "Error when reading response body for URL {Url}", e.Response.Url);
            }
        } catch (Exception ex) {
            // Catch-all for any unexpected exceptions in the event handler
            // This prevents async void from crashing the application
            _logger.LogError(ex, "Unexpected error in ProcessPageResponse for instrument {Instrument}", _instrumentDto);
        }
    }

    private void ProcessOutputs(IList<TsxCompanyProcessorFsmOutputBase> outputList) {
        string? lastError = null;
        foreach (TsxCompanyProcessorFsmOutputBase output in outputList) {
            switch (output) {
                case TsxCompanyProcessorFsmOutputInProgress: break; // Progress report
                case TsxCompanyProcessorFsmOutputCompanyRawFinancials crf: // Processing complete
                    CompanyReport = crf.CompanyReport;
                    _ = _attemptDone?.TrySetResult(null);
                    break;
                case TsxCompanyProcessorFsmOutputEncounteredError err:
                    lastError = err.ErrMsg;
                    break;
                default: break;
            }
        }

        // An unusable response for this instrument ends the attempt now instead of at its timeout (review P11).
        // Errors that leave the parser running (a response for another symbol) are only logged by the parser.
        if (_fsm.IsInError)
            _ = _attemptDone?.TrySetResult(lastError ?? "Unusable response");
    }

    /// <summary>Starts a scrape attempt on the processor's loop (see <see cref="BeginAttempt"/>).</summary>
    private sealed class StartAttemptInput(int attempt, TaskCompletionSource<string?> done) : TsxCompanyProcessorFsmInputBase {
        public int Attempt { get; } = attempt;
        public TaskCompletionSource<string?> Done { get; } = done;
    }

    public override void Dispose() {
        _ = _inputChannel.Writer.TryComplete();
        base.Dispose();
    }
}
