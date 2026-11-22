using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The scrape parser: it turns the three JSON responses the TMX page loads (quote, quarterly and annual financials)
/// into one company's raw data. Code review R21: it had no tests although it needs no browser or network.
/// </summary>
public class TsxCompanyProcessorFsmTests {
    private const string Symbol = "ABC";

    // Shapes of the real URLs (from the production logs), cut down: the symbol comes late in a long query string.
    private const string FinancialsUrl = "https://app.quotemedia.com/datatool/getFinancialsEnhancedBySymbol.json?currency=true&defaultUnit=Millions&lang=en&numberOfReports=5&pathName=%2Fen%2Fquote%2FABC%2Ffinancials-filings&reportOrder=D&reportType={0}&symbol={1}&typeList=IncomeStatement%2CBalanceSheet%2CCashFlow&webmasterId=101020";
    private const string QuoteUrl = "https://app.quotemedia.com/datatool/getEnhancedQuotes.json?timezone=true&afterhours=true&premarket=true&currencyInd=true&symbols={0}&webmasterId=101020";

    private const string QuoteBody = """
        {"results":{"quote":[{"key":{"symbol":"ABC"},"pricedata":{"last":12.5},"fundamental":{"totalsharesoutstanding":1000000}}]}}
        """;

    private static string ReportsBody(params string[] reportDates) =>
        "{\"results\":{\"Company\":{\"Report\":[" + string.Join(",", reportDates.Select(d =>
            "{\"reportDate\":\"" + d + "\",\"CashFlow\":{\"NetIncome\":5,\"Note\":\"text\"},\"IncomeStatement\":{\"Revenue\":10},\"BalanceSheet\":{\"TotalAssets\":100}}")) + "]}}}";

    private static TsxCompanyProcessorFsm NewFsm() =>
        new(new InstrumentDto(1, "TSX", Symbol, "ABC Inc.", Symbol, "ABC Common", DateTimeOffset.UtcNow, null),
            NullLogger<TsxCompanyProcessorFsm>.Instance);

    private static GotResponse Quote(string body = QuoteBody, string symbol = Symbol) =>
        new() { Url = string.Format(QuoteUrl, symbol), Text = body };

    private static GotResponse Financials(char reportType, string body, string symbol = Symbol) =>
        new() { Url = string.Format(FinancialsUrl, reportType, symbol), Text = body };

    private static IList<TsxCompanyProcessorFsmOutputBase> FeedAll(TsxCompanyProcessorFsm fsm) {
        _ = fsm.Update(Quote());
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31", "2025-09-30")));
        return fsm.Update(Financials('A', ReportsBody("2025-12-31")));
    }

    [Fact]
    public void AllThreeResponses_ProduceTheCompanyData() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        IList<TsxCompanyProcessorFsmOutputBase> outputs = FeedAll(fsm);

        fsm.IsDone.Should().BeTrue();
        TsxCompanyData data = outputs.OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Single().CompanyReport;
        data.Symbol.Should().Be(Symbol);
        data.PricePerShare.Should().Be(12.5m);
        data.CurNumShares.Should().Be(1_000_000UL);
        data.QuarterlyRawCashFlowReports.Should().HaveCount(2);
        data.AnnualRawBalanceSheets.Should().ContainSingle().Which["TotalAssets"].Should().Be(100m);
    }

    [Fact]
    public void ResponsesInAnyOrder_ProduceTheCompanyData() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        _ = fsm.Update(Financials('A', ReportsBody("2025-12-31")));
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31")));
        fsm.IsDone.Should().BeFalse("the quote has not arrived yet");
        IList<TsxCompanyProcessorFsmOutputBase> outputs = fsm.Update(Quote());

        outputs.OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Should().ContainSingle();
    }

    [Fact]
    public void NonNumericFields_AreLeftOut() {
        TsxCompanyData data = FeedAll(NewFsm()).OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Single().CompanyReport;

        data.AnnualRawCashFlowReports.Single().HasValue("Note").Should().BeFalse();
        data.AnnualRawCashFlowReports.Single()["NetIncome"].Should().Be(5m);
    }

    [Fact]
    public void ReportWithAMissingOrBadDate_IsSkipped() {
        TsxCompanyProcessorFsm fsm = NewFsm();
        _ = fsm.Update(Quote());
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31", "31/12/2025")));
        string noDate = "{\"results\":{\"Company\":{\"Report\":[{\"CashFlow\":{},\"IncomeStatement\":{},\"BalanceSheet\":{}}]}}}";

        TsxCompanyData data = fsm.Update(Financials('A', noDate)).OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Single().CompanyReport;

        data.QuarterlyRawCashFlowReports.Should().ContainSingle().Which.ReportDate.Should().Be(new DateOnly(2025, 12, 31));
        data.AnnualRawCashFlowReports.Should().BeEmpty();
    }

    [Fact]
    public void SharesOutstanding_FallsBackToSharesoutstanding() {
        TsxCompanyProcessorFsm fsm = NewFsm();
        string body = QuoteBody.Replace("totalsharesoutstanding", "sharesoutstanding");

        _ = fsm.Update(Quote(body));
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31")));
        TsxCompanyData data = fsm.Update(Financials('A', ReportsBody("2025-12-31"))).OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Single().CompanyReport;

        data.CurNumShares.Should().Be(1_000_000UL);
    }

    [Fact]
    public void ResponseForAnotherSymbol_IsReportedAndIgnored() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        IList<TsxCompanyProcessorFsmOutputBase> outputs = fsm.Update(Quote(symbol: "XYZ"));

        outputs.Should().ContainSingle().Which.Should().BeOfType<TsxCompanyProcessorFsmOutputEncounteredError>();
        fsm.IsDone.Should().BeFalse();
        // The real quote still completes the company afterwards.
        _ = fsm.Update(Quote());
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31")));
        fsm.Update(Financials('A', ReportsBody("2025-12-31"))).OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("""{"results":{"Company":{}}}""")]                // no report list, as for XNDU in the logs
    [InlineData("""{"results":{"Company":{"Report":null}}}""")]
    [InlineData("not json")]
    public void BadFinancialsBody_IsReportedAsAnError(string body) {
        TsxCompanyProcessorFsm fsm = NewFsm();

        IList<TsxCompanyProcessorFsmOutputBase> outputs = fsm.Update(Financials('Q', body));

        outputs.OfType<TsxCompanyProcessorFsmOutputEncounteredError>().Should().NotBeEmpty();
        fsm.GotAllData.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"results":{"quote":[]}}""")]
    [InlineData("""{"results":{"quote":[{"key":{},"fundamental":{"totalsharesoutstanding":1}}]}}""")]      // no price
    [InlineData("""{"results":{"quote":[{"key":{},"pricedata":{"last":1}}]}}""")]                          // no fundamental
    [InlineData("""{"results":{"quote":[{"key":{},"pricedata":{"last":1},"fundamental":{}}]}}""")]        // no shares
    [InlineData("""{"results":{"quote":[{"key":{},"pricedata":{"last":1},"fundamental":{"totalsharesoutstanding":"n/a"}}]}}""")]
    public void BadQuoteBody_IsReportedAsAnErrorAndRecordsNothing(string body) {
        TsxCompanyProcessorFsm fsm = NewFsm();

        IList<TsxCompanyProcessorFsmOutputBase> outputs = fsm.Update(Quote(body));
        _ = fsm.Update(Financials('Q', ReportsBody("2025-12-31")));
        IList<TsxCompanyProcessorFsmOutputBase> last = fsm.Update(Financials('A', ReportsBody("2025-12-31")));

        outputs.OfType<TsxCompanyProcessorFsmOutputEncounteredError>().Should().NotBeEmpty();
        last.OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Should().BeEmpty("the quote never arrived intact");
    }

    [Fact]
    public void ResponseAfterTheCompanyIsDone_IsAnError() {
        TsxCompanyProcessorFsm fsm = NewFsm();
        _ = FeedAll(fsm);

        fsm.Update(Quote()).Should().ContainSingle().Which.Should().BeOfType<TsxCompanyProcessorFsmOutputEncounteredError>();
    }

    [Fact]
    public void Reset_StartsOver() {
        TsxCompanyProcessorFsm fsm = NewFsm();
        _ = FeedAll(fsm);

        fsm.Reset();

        fsm.IsDone.Should().BeFalse();
        fsm.GotAllData.Should().BeFalse();
        FeedAll(fsm).OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Should().ContainSingle();
    }

    // Review P14: the whole URL went to ParseQueryString, so a first parameter's key came out as "https://...json?symbol"
    // and the response was rejected. Today's URLs put the symbol late; this guards against a reordering.
    [Fact]
    public void SymbolAsTheFirstQueryParameter_IsAccepted() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        _ = fsm.Update(new GotResponse { Url = $"https://app.quotemedia.com/datatool/getEnhancedQuotes.json?symbols={Symbol}&webmasterId=1", Text = QuoteBody });
        _ = fsm.Update(new GotResponse { Url = $"https://app.quotemedia.com/datatool/getFinancialsEnhancedBySymbol.json?symbol={Symbol}&reportType=Q", Text = ReportsBody("2025-12-31") });
        IList<TsxCompanyProcessorFsmOutputBase> outputs = fsm.Update(new GotResponse { Url = $"https://app.quotemedia.com/datatool/getFinancialsEnhancedBySymbol.json?symbol={Symbol}&reportType=A", Text = ReportsBody("2025-12-31") });

        outputs.OfType<TsxCompanyProcessorFsmOutputCompanyRawFinancials>().Should().ContainSingle();
    }

    // Review P11: an error in our own symbol's response never ended the scrape, so each one waited out the 60 s timeout.
    [Theory]
    [InlineData('Q', """{"results":{"Company":{}}}""")]
    [InlineData('A', "not json")]
    public void BadBodyForOurSymbol_PutsTheParserInError(char reportType, string body) {
        TsxCompanyProcessorFsm fsm = NewFsm();

        _ = fsm.Update(Financials(reportType, body));

        fsm.IsInError.Should().BeTrue();
        fsm.IsDone.Should().BeTrue();
    }

    [Fact]
    public void BadQuoteForOurSymbol_PutsTheParserInError() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        _ = fsm.Update(Quote("""{"results":{"quote":[]}}"""));

        fsm.IsInError.Should().BeTrue();
    }

    [Fact]
    public void ResponseForAnotherSymbol_DoesNotPutTheParserInError() {
        TsxCompanyProcessorFsm fsm = NewFsm();

        _ = fsm.Update(Quote(symbol: "XYZ"));

        fsm.IsInError.Should().BeFalse();
    }
}
