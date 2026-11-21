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
    private const string FixtureRelativePath = "deep-value/src/testing/company-full-detail-report.json";

    // ASP.NET Core controllers serialize with JsonSerializerDefaults.Web (camelCase names).
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// A fictional company that passes 12 of 15 checks: it fails est. return (CF) > 5% and both ROE checks
    /// (ROE from owner earnings is exactly 10%, which does not pass "> 10%").
    /// </summary>
    internal static CompanyFullDetailReport SampleReport() => new(
        exchange: "TSX",
        companySymbol: "EXMP",
        instrumentSymbol: "EXMP",
        companyName: "Example Corp",
        instrumentName: "Example Corp Common",
        pricePerShare: 25M,
        curLongTermDebt: 300_000_000M,
        curTotalShareholdersEquity: 900_000_000M,
        curBookValue: 800_000_000M,
        curNumShares: 50_000_000,
        averageNetCashFlow: 40_000_000M,
        averageOwnerEarnings: 90_000_000M,
        curDividendsPaid: 20_000_000M,
        curAdjustedRetainedEarnings: 400_000_000M,
        oldestRetainedEarnings: 250_000_000M,
        numAnnualProcessedCashFlowReports: 6,
        mostRecentAnnualReportDate: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
        mostRecentQuarterlyReportDate: new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero),
        lastUpdatedDate: new DateTimeOffset(2026, 8, 26, 2, 2, 35, TimeSpan.Zero));

    [Fact]
    public void SampleReport_ScoresTwelve() {
        CompanyFullDetailReport report = SampleReport();

        _ = report.OverallScore.Should().Be(12);
        _ = report.DoesPassCheck_EstNextYearTotalReturn_CashFlow_BigEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromCashFlow_BigEnough.Should().BeFalse();
        _ = report.DoesPassCheck_ROE_FromOwnerEarnings_BigEnough.Should().BeFalse();
    }

    [Fact]
    public void DetailsJson_MatchesSharedFixture() {
        string actualJson = JsonSerializer.Serialize(SampleReport(), WebJsonOptions);
        string fixturePath = Path.Combine(FindRepoRoot(), FixtureRelativePath);

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
