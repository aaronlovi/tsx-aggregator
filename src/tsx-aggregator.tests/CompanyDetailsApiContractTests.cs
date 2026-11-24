using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

/// <summary>
/// Pins the JSON contract of GET companies/{exchange}/{symbol} between the backend and the Angular app.
/// The same fixture file is used by deep-value's company.service.spec.ts, so a change on either side that
/// breaks the other fails a test. Set TSX_UPDATE_FIXTURES=1 to regenerate the fixture after a deliberate change.
/// </summary>
public class CompanyDetailsApiContractTests {
    private const string FixtureDirectory = "deep-value/src/testing";
    private const string FixtureFileName = "company-full-detail-report.json";
    private const string MissingDataFixtureFileName = "company-full-detail-report-missing-data.json";

    // ASP.NET Core controllers serialize with JsonSerializerDefaults.Web (camelCase names).
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// A fictional company that passes 13 of 16 checks: it fails est. return (CF) > 5% and both ROE checks
    /// (ROE from owner earnings is exactly 10%, which does not pass "> 10%"), and passes check 16 (4 of 5 years).
    /// </summary>
    internal static CompanyFullDetailReport SampleReport() => new(
        exchange: "TSX",
        companySymbol: "EXMP",
        instrumentSymbol: "EXMP",
        companyName: "Example Corp",
        instrumentName: "Example Corp Common",
        pricePerShare: 25M,
        curLongTermDebt: 300_000_000M,
        curShortTermDebt: 0M,
        curTotalShareholdersEquity: 900_000_000M,
        curBookValue: 800_000_000M,
        curNumShares: 50_000_000,
        averageNetCashFlow: 40_000_000M,
        averageOwnerEarnings: 90_000_000M,
        latestOwnerEarnings: 95_000_000M,
        latestNetCashFlow: 30_000_000M,
        curDividendsPaid: 20_000_000M,
        averageDividendsPaid: 20_000_000M,
        curAdjustedRetainedEarnings: 400_000_000M,
        oldestRetainedEarnings: 250_000_000M,
        numAnnualProcessedCashFlowReports: 6,
        roe: new RoeInputs(
            // ROE over matched years (B11): 6.7% from cash flow (NCF + dividends), exactly 10% from owner earnings.
            CashFlowNumerator: 360_000_000M,
            CashFlowEquity: 5_400_000_000M,
            OwnerEarningsNumerator: 540_000_000M,
            OwnerEarningsEquity: 5_400_000_000M,
            // Check 16 (C16): ROE (OE) of the last five years, most recent first: 12%, 11%, 9%, 13%, 10.5%, so 4 of 5 over 10%.
            OwnerEarningsByYear: [
                new AnnualReturnOnEquity(new DateOnly(2025, 12, 31), 0.12M),
                new AnnualReturnOnEquity(new DateOnly(2024, 12, 31), 0.11M),
                new AnnualReturnOnEquity(new DateOnly(2023, 12, 31), 0.09M),
                new AnnualReturnOnEquity(new DateOnly(2022, 12, 31), 0.13M),
                new AnnualReturnOnEquity(new DateOnly(2021, 12, 31), 0.105M)]),
        scoring: new ScoringOptions(),
        mostRecentAnnualReportDate: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
        mostRecentQuarterlyReportDate: new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
        lastUpdatedDate: new DateTimeOffset(2026, 8, 26, 2, 2, 35, TimeSpan.Zero));

    [Fact]
    public void SampleReport_ScoresThirteen() {
        CompanyFullDetailReport report = SampleReport();

        _ = report.OverallScore.Should().Be(13);
        _ = report.RoeOwnerEarningsYearsAbove10.Should().Be(4);
        _ = report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().Be(CheckResult.Fail);
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().Be(CheckResult.Fail);
    }

    /// <summary>
    /// The sample company with no equity (so no book value) and no oldest retained earnings in the source data:
    /// checks 1, 2, 3, 10, 13, 14 and 15 are NotAvailable, and max price and upside are null (W1). Its current
    /// retained earnings are estimated (decision 23).
    /// </summary>
    internal static CompanyFullDetailReport MissingDataSampleReport() {
        CompanyFullDetailReport sample = SampleReport();
        return new CompanyFullDetailReport(
            exchange: sample.Exchange,
            companySymbol: sample.CompanySymbol,
            instrumentSymbol: sample.InstrumentSymbol,
            companyName: sample.CompanyName,
            instrumentName: sample.InstrumentName,
            pricePerShare: sample.PricePerShare,
            curLongTermDebt: sample.CurLongTermDebt,
            curShortTermDebt: 0M,
            curTotalShareholdersEquity: null,
            curBookValue: null,
            curNumShares: sample.CurNumShares,
            averageNetCashFlow: sample.AverageNetCashFlow,
            averageOwnerEarnings: sample.AverageOwnerEarnings,
            latestOwnerEarnings: null,
            latestNetCashFlow: null,
            curDividendsPaid: sample.CurDividendsPaid,
            averageDividendsPaid: sample.CurDividendsPaid,
            curAdjustedRetainedEarnings: sample.CurAdjustedRetainedEarnings,
            oldestRetainedEarnings: null,
            numAnnualProcessedCashFlowReports: sample.NumAnnualProcessedCashFlowReports,
            roe: new RoeInputs(
                CashFlowNumerator: sample.RoeCashFlowNumerator,
                CashFlowEquity: null,
                OwnerEarningsNumerator: sample.RoeOwnerEarningsNumerator,
                OwnerEarningsEquity: null,
                OwnerEarningsByYear: sample.RoeOwnerEarningsByYear),
            scoring: new ScoringOptions(),
            mostRecentAnnualReportDate: sample.MostRecentAnnualReportDate,
            mostRecentQuarterlyReportDate: sample.MostRecentQuarterlyReportDate,
            lastUpdatedDate: sample.LastUpdatedDate,
            curRetainedEarningsEstimated: true);
    }

    [Fact]
    public void MissingDataSampleReport_HasNotAvailableChecks() {
        CompanyFullDetailReport report = MissingDataSampleReport();

        _ = report.NotAvailableCount.Should().Be(7);
        _ = report.OverallScore.Should().Be(8);
        _ = report.MaxPrice.Should().BeNull();
    }

    [Fact]
    public void DetailsJson_MatchesSharedFixture() => AssertMatchesFixture(SampleReport(), FixtureFileName);

    [Fact]
    public void MissingDataDetailsJson_MatchesSharedFixture() => AssertMatchesFixture(MissingDataSampleReport(), MissingDataFixtureFileName);

    private static void AssertMatchesFixture(CompanyFullDetailReport report, string fixtureFileName) {
        string actualJson = JsonSerializer.Serialize(report, WebJsonOptions);
        string fixturePath = Path.Combine(FindRepoRoot(), FixtureDirectory, fixtureFileName);

        if (Environment.GetEnvironmentVariable("TSX_UPDATE_FIXTURES") == "1") {
            File.WriteAllText(fixturePath, actualJson + "\n");
            return;
        }

        JsonObject actual = JsonNode.Parse(actualJson)!.AsObject();
        JsonObject expected = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();

        _ = actual.Select(p => p.Key).Should().BeEquivalentTo(expected.Select(p => p.Key),
            "the Angular app binds to these property names; regenerate the fixture with TSX_UPDATE_FIXTURES=1 only for a deliberate API change");

        foreach ((string name, JsonNode? expectedValue) in expected) {
            JsonNode? actualValue = actual[name];
            if (expectedValue is JsonValue ev && ev.TryGetValue(out decimal expectedNumber)) {
                _ = actualValue!.GetValue<decimal>().Should().Be(expectedNumber, $"property {name}");
            } else {
                _ = JsonNode.DeepEquals(actualValue, expectedValue).Should().BeTrue($"property {name} should be {expectedValue?.ToJsonString()}, was {actualValue?.ToJsonString()}");
            }
        }
    }

    private static string FindRepoRoot() {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "deep-value")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the repo root (a directory containing deep-value/)");
    }
}
