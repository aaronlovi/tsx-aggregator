using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using tsx_aggregator.models;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The company processor's handling of responses, without a browser: responses are fed straight into its loop.
/// Code review P11 (an error ended nothing, so each failed scrape waited the full timeout) and R8 (a retry reset the
/// parser from the caller's thread while late responses from the previous page could still arrive).
/// </summary>
public sealed class TsxCompanyProcessorTests : IAsyncLifetime {
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private const string Url = "https://app.quotemedia.com/datatool/getFinancialsEnhancedBySymbol.json?reportType={0}&symbol=ABC";
    private const string QuoteUrl = "https://app.quotemedia.com/datatool/getEnhancedQuotes.json?symbols=ABC";
    private const string Reports = """{"results":{"Company":{"Report":[{"reportDate":"2025-12-31","CashFlow":{"A":1},"IncomeStatement":{"B":2},"BalanceSheet":{"C":3}}]}}}""";
    private const string QuoteBody = """{"results":{"quote":[{"key":{},"pricedata":{"last":2},"fundamental":{"totalsharesoutstanding":10}}]}}""";

    private readonly CancellationTokenSource _stop = new();
    private TsxCompanyProcessor? _processor;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        await _stop.CancelAsync();
        if (_processor is not null) {
            await _processor.StopAsync(CancellationToken.None);
            _processor.Dispose();
        }
        _stop.Dispose();
    }

    private async Task<TsxCompanyProcessor> Start() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<TsxCompanyProcessor>>(NullLogger<TsxCompanyProcessor>.Instance);
        _ = services.AddSingleton<ILogger<TsxCompanyProcessorFsm>>(NullLogger<TsxCompanyProcessorFsm>.Instance);
        // An installed browser path, so Create does not download one; no browser is launched in these tests.
        _ = services.AddSingleton(Options.Create(new ScraperOptions { BrowserExecutablePath = "/usr/bin/true" }));
        var instrument = new InstrumentDto(1, "TSX", "ABC", "ABC Inc.", "ABC", "ABC Common", DateTimeOffset.UtcNow, null);
        _processor = await TsxCompanyProcessor.Create(instrument, services.BuildServiceProvider(), _stop.Token);
        await _processor.StartAsync(_stop.Token);
        return _processor;
    }

    private static GotResponse Financials(int attempt, char type, string body) =>
        new() { Attempt = attempt, Url = string.Format(Url, type), Text = body };

    [Fact]
    public async Task AllResponses_CompleteTheAttemptWithTheData() {
        TsxCompanyProcessor processor = await Start();
        Task<string?> attempt = processor.BeginAttempt(1);

        processor.PostResponse(new GotResponse { Attempt = 1, Url = QuoteUrl, Text = QuoteBody }).Should().BeTrue();
        _ = processor.PostResponse(Financials(1, 'Q', Reports));
        _ = processor.PostResponse(Financials(1, 'A', Reports));

        (await attempt.WaitAsync(Deadline)).Should().BeNull("no error");
        processor.CompanyReport!.PricePerShare.Should().Be(2m);
    }

    [Fact]
    public async Task ErrorInOurResponse_EndsTheAttemptAtOnce() {
        TsxCompanyProcessor processor = await Start();
        Task<string?> attempt = processor.BeginAttempt(1);

        _ = processor.PostResponse(Financials(1, 'Q', """{"results":{"Company":{}}}"""));

        (await attempt.WaitAsync(Deadline)).Should().Contain("Invalid body");
    }

    [Fact]
    public async Task ResponseFromAnEarlierAttempt_IsIgnored() {
        TsxCompanyProcessor processor = await Start();
        _ = processor.BeginAttempt(1);
        Task<string?> second = processor.BeginAttempt(2);

        // A late error from the first page must not end the second attempt...
        _ = processor.PostResponse(Financials(1, 'Q', """{"results":{"Company":{}}}"""));
        // ...and the second attempt's own responses still complete it.
        _ = processor.PostResponse(new GotResponse { Attempt = 2, Url = QuoteUrl, Text = QuoteBody });
        _ = processor.PostResponse(Financials(2, 'Q', Reports));
        _ = processor.PostResponse(Financials(2, 'A', Reports));

        (await second.WaitAsync(Deadline)).Should().BeNull();
    }
}
