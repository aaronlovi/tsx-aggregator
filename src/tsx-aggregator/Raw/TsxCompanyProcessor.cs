using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger _logger;
    private readonly InstrumentDto _instrumentDto;
    private readonly TsxCompanyProcessorFsm _fsm;
    private readonly TaskCompletionSource _tcs;
    private readonly Channel<TsxCompanyProcessorFsmInputBase> _inputChannel;
    private readonly CancellationToken _externalCancellationToken;
    private TsxCompanyData? _companyReport;

    private TsxCompanyProcessor(
        InstrumentDto instrumentDto,
        IServiceProvider svp,
        CancellationToken externalCancellationToken) {
        _logger = svp.GetRequiredService<ILogger<TsxCompanyProcessor>>();
        _instrumentDto = instrumentDto;
        _fsm = new(_instrumentDto, svp.GetRequiredService<ILogger<TsxCompanyProcessorFsm>>());
        _tcs = new();
        _inputChannel = Channel.CreateUnbounded<TsxCompanyProcessorFsmInputBase>();
        _externalCancellationToken = externalCancellationToken;
    }

    public bool IsFaulted { get; private set; }
    public TsxCompanyData? CompanyReport => _companyReport;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _externalCancellationToken);
        CancellationToken ct = cts.Token;

        try {
            while (!ct.IsCancellationRequested) {
                await foreach (TsxCompanyProcessorFsmInputBase input in _inputChannel.Reader.ReadAllAsync(ct)) {
                    IList<TsxCompanyProcessorFsmOutputBase> outputList = _fsm.Update(input);
                    ProcessOutputs(outputList);
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
        await Init();
        var processor = new TsxCompanyProcessor(instrumentDto, svp, externalCancellationToken);
        return processor;
    }

    public async Task<Result<TsxCompanyData>> GetRawFinancials() {
        TimeSpan runTime = TimeSpan.Zero;
        DateTime dateTimstart = DateTime.UtcNow;

        try {
            var launchOptions = new LaunchOptions { Headless = true };
            using IBrowser browser = await Puppeteer.LaunchAsync(launchOptions);
            using IPage page = await browser.NewPageAsync();
            page.DefaultNavigationTimeout = 60000;
            page.Response += ProcessPageResponse;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _ = await page.GoToAsync($"https://money.tmx.com/en/quote/{_instrumentDto.InstrumentSymbol}/financials-filings");

            await _tcs.Task.WaitAsync(cts.Token);

            page.Response -= ProcessPageResponse;
            await browser.CloseAsync();

            return Result<TsxCompanyData>.SetSuccess(CompanyReport!);
        }
        catch (OperationCanceledException) {
            _logger.LogInformation("GetRawFinancials timed out for instrument {Instrument}", _instrumentDto);
            return Result<TsxCompanyData>.SetFailure("Timed out");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "GetRawFinancials general fault for instrument {Instrument}", _instrumentDto);
            return Result<TsxCompanyData>.SetFailure("General fault: " + ex.Message);
        }
    }

    private static async Task Init() {
        using var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();
    }

    private async void ProcessPageResponse(object? sender, ResponseCreatedEventArgs e) {
        if (e.Response.Status != HttpStatusCode.OK)
            return;

        //_logger.LogInformation("Response received for {Url},{IsEnhancedQuotes},{IsFinancialsEnhanced}",
        //    e.Response.Url,
        //    e.Response.Url.Contains("getEnhancedQuotes.json"),
        //    e.Response.Url.Contains("getFinancialsEnhancedBySymbol.json"));

        if (!e.Response.Url.Contains("getEnhancedQuotes.json")
            && !e.Response.Url.Contains("getFinancialsEnhancedBySymbol.json"))
            return;

        var headers = e.Response.Headers;
        if (!headers.ContainsKey("content-encoding"))
            return;

        try {
            string responseText = await e.Response.TextAsync();
            _ = _inputChannel.Writer.TryWrite(new GotResponse() {
                Text = responseText,
                Url = e.Response.Url
            });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error when reading response body for URL {Url}", e.Response.Url);
        }
    }

    private void ProcessOutputs(IList<TsxCompanyProcessorFsmOutputBase> outputList) {
        foreach (TsxCompanyProcessorFsmOutputBase output in outputList) {
            switch (output) {
                case TsxCompanyProcessorFsmOutputInProgress: break; // Progress report
                case TsxCompanyProcessorFsmOutputCompanyRawFinancials crf: // Processing complete
                    _companyReport = crf.CompanyReport;
                    _ = _tcs.TrySetResult();
                    break;
                case TsxCompanyProcessorFsmOutputEncounteredError:
                    break; // Error
                default: break;
            }
        }
    }

    public override void Dispose() {
        _ = _inputChannel.Writer.TryComplete();
        base.Dispose();
    }
}
