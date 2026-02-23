using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class TopScoreDiffComputerTests {

    /// <summary>
    /// Creates a CompanyFullDetailReport that passes all 15 checks (OverallScore == 15).
    /// </summary>
    private static CompanyFullDetailReport MakeTopScoreCompany(string symbol, string name) {
        return new CompanyFullDetailReport(
            exchange: "TSX",
            companySymbol: symbol.Replace(".TO", ""),
            instrumentSymbol: symbol,
            companyName: name,
            instrumentName: $"{name} Common Shares",
            pricePerShare: 50M,
            curLongTermDebt: 100_000_000M,
            curTotalShareholdersEquity: 500_000_000M,
            curBookValue: 200_000_000M,
            curNumShares: 10_000_000,
            averageNetCashFlow: 60_000_000M,
            averageOwnerEarnings: 55_000_000M,
            curDividendsPaid: 5_000_000M,
            curAdjustedRetainedEarnings: 50_000_000M,
            oldestRetainedEarnings: 30_000_000M,
            numAnnualProcessedCashFlowReports: 5);
    }

    /// <summary>
    /// Creates a CompanyFullDetailReport that fails multiple checks (low score).
    /// PricePerShare=0 means CurMarketCap=0, failing price-dependent checks.
    /// </summary>
    private static CompanyFullDetailReport MakeLowScoreCompany(string symbol, string name) {
        return new CompanyFullDetailReport(
            exchange: "TSX",
            companySymbol: symbol.Replace(".TO", ""),
            instrumentSymbol: symbol,
            companyName: name,
            instrumentName: $"{name} Common Shares",
            pricePerShare: 0M,
            curLongTermDebt: 100_000_000M,
            curTotalShareholdersEquity: 500_000_000M,
            curBookValue: 200_000_000M,
            curNumShares: 10_000_000,
            averageNetCashFlow: 40_000_000M,
            averageOwnerEarnings: 35_000_000M,
            curDividendsPaid: 5_000_000M,
            curAdjustedRetainedEarnings: 50_000_000M,
            oldestRetainedEarnings: 30_000_000M,
            numAnnualProcessedCashFlowReports: 5);
    }

    /// <summary>
    /// Helper to create a TopScoreCompany with default financial data for diff tests.
    /// </summary>
    private static TopScoreCompany MakeTopScoreCompanyRecord(string symbol, string name) {
        return new TopScoreCompany(symbol, name, 50M, 60M, 20M, 500_000_000M, 7M, 6M, 15);
    }

    // --- ComputeTopScoreList tests ---

    [Fact]
    public void ComputeTopScoreList_WithMixedScores_ReturnsOnlyTopScoreCompanies() {
        // Arrange
        var reports = new List<CompanyFullDetailReport> {
            MakeTopScoreCompany("ABC.TO", "ABC Corp"),
            MakeLowScoreCompany("DEF.TO", "DEF Inc"),
            MakeTopScoreCompany("GHI.TO", "GHI Ltd"),
            MakeLowScoreCompany("JKL.TO", "JKL Holdings")
        };

        // Act
        var result = TopScoreDiffComputer.ComputeTopScoreList(reports);

        // Assert
        _ = result.Should().HaveCount(2);
        _ = result[0].InstrumentSymbol.Should().Be("ABC.TO");
        _ = result[1].InstrumentSymbol.Should().Be("GHI.TO");
    }

    [Fact]
    public void ComputeTopScoreList_WithEmptyInput_ReturnsEmptyList() {
        var result = TopScoreDiffComputer.ComputeTopScoreList([]);
        _ = result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeTopScoreList_WithNoTopScoreCompanies_ReturnsEmptyList() {
        var reports = new List<CompanyFullDetailReport> {
            MakeLowScoreCompany("ABC.TO", "ABC Corp"),
            MakeLowScoreCompany("DEF.TO", "DEF Inc")
        };

        var result = TopScoreDiffComputer.ComputeTopScoreList(reports);
        _ = result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeTopScoreList_ReturnsSortedBySymbol() {
        var reports = new List<CompanyFullDetailReport> {
            MakeTopScoreCompany("ZZZ.TO", "ZZZ Corp"),
            MakeTopScoreCompany("AAA.TO", "AAA Inc"),
            MakeTopScoreCompany("MMM.TO", "MMM Ltd")
        };

        var result = TopScoreDiffComputer.ComputeTopScoreList(reports);
        _ = result.Should().HaveCount(3);
        _ = result[0].InstrumentSymbol.Should().Be("AAA.TO");
        _ = result[1].InstrumentSymbol.Should().Be("MMM.TO");
        _ = result[2].InstrumentSymbol.Should().Be("ZZZ.TO");
    }

    [Fact]
    public void ComputeTopScoreList_PopulatesFinancialFields() {
        var report = MakeTopScoreCompany("ABC.TO", "ABC Corp");
        var result = TopScoreDiffComputer.ComputeTopScoreList([report]);

        _ = result.Should().HaveCount(1);
        _ = result[0].PricePerShare.Should().Be(report.PricePerShare);
        _ = result[0].MaxPrice.Should().Be(report.MaxPrice);
        _ = result[0].PercentageUpside.Should().Be(report.PercentageUpside);
        _ = result[0].CurMarketCap.Should().Be(report.CurMarketCap);
        _ = result[0].EstReturnCashFlow.Should().Be(report.EstimatedNextYearTotalReturnPercentage_FromCashFlow);
        _ = result[0].EstReturnOwnerEarnings.Should().Be(report.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings);
        _ = result[0].OverallScore.Should().Be(15);
    }

    [Fact]
    public void ComputeTopScoreList_CompanyWithZeroPrice_IsExcluded() {
        // A company with PricePerShare=0 can't reach score 13
        var report = MakeLowScoreCompany("ABC.TO", "ABC Corp");
        _ = report.OverallScore.Should().BeLessThan(13, "a company with no price data cannot reach score 13");

        var result = TopScoreDiffComputer.ComputeTopScoreList([report]);
        _ = result.Should().BeEmpty();
    }

    // --- ComputeDiff tests ---

    [Fact]
    public void ComputeDiff_IdenticalLists_ReturnsNull() {
        var list = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")
        };

        var result = TopScoreDiffComputer.ComputeDiff(list, list);
        _ = result.Should().BeNull();
    }

    [Fact]
    public void ComputeDiff_AdditionsOnly_ReturnsCorrectDiff() {
        var previous = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")
        };
        var current = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc"),
            MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")
        };

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(2);
        _ = result.Added[0].InstrumentSymbol.Should().Be("DEF.TO");
        _ = result.Added[1].InstrumentSymbol.Should().Be("GHI.TO");
        _ = result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_RemovalsOnly_ReturnsCorrectDiff() {
        var previous = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc"),
            MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")
        };
        var current = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")
        };

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().BeEmpty();
        _ = result.Removed.Should().HaveCount(2);
        _ = result.Removed[0].InstrumentSymbol.Should().Be("DEF.TO");
        _ = result.Removed[1].InstrumentSymbol.Should().Be("GHI.TO");
    }

    [Fact]
    public void ComputeDiff_BothAdditionsAndRemovals_ReturnsCorrectDiff() {
        var previous = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")
        };
        var current = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")
        };

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(1);
        _ = result.Added[0].InstrumentSymbol.Should().Be("GHI.TO");
        _ = result.Removed.Should().HaveCount(1);
        _ = result.Removed[0].InstrumentSymbol.Should().Be("DEF.TO");
    }

    [Fact]
    public void ComputeDiff_FromEmptyToNonEmpty_AllItemsInAdded() {
        var previous = new List<TopScoreCompany>();
        var current = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")
        };

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(2);
        _ = result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_FromNonEmptyToEmpty_AllItemsInRemoved() {
        var previous = new List<TopScoreCompany> {
            MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"),
            MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")
        };
        var current = new List<TopScoreCompany>();

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().BeEmpty();
        _ = result.Removed.Should().HaveCount(2);
    }

    [Fact]
    public void ComputeDiff_SameSymbolDifferentPrices_ReturnsNull() {
        // Equality is based on InstrumentSymbol only, so different prices should not create a diff
        var previous = new List<TopScoreCompany> {
            new("ABC.TO", "ABC Corp", 50M, 60M, 20M, 500_000_000M, 7M, 6M, 15)
        };
        var current = new List<TopScoreCompany> {
            new("ABC.TO", "ABC Corp", 55M, 65M, 18M, 550_000_000M, 8M, 7M, 15)
        };

        var result = TopScoreDiffComputer.ComputeDiff(previous, current);
        _ = result.Should().BeNull();
    }

    // --- FormatAlertSubject tests ---

    [Fact]
    public void FormatAlertSubject_WithKnownDiff_ReturnsCorrectSubject() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc"), MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Added: [MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc"), MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Removed: []);

        var subject = TopScoreDiffComputer.FormatAlertSubject(diff);
        _ = subject.Should().Be("TSX Top-Score Alert: 2 added, 0 removed");
    }

    [Fact]
    public void FormatAlertSubject_WithAdditionsAndRemovals_ReturnsCorrectSubject() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("PQR.TO", "PQR Industries")],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Added: [MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Removed: [MakeTopScoreCompanyRecord("PQR.TO", "PQR Industries")]);

        var subject = TopScoreDiffComputer.FormatAlertSubject(diff);
        _ = subject.Should().Be("TSX Top-Score Alert: 1 added, 1 removed");
    }

    // --- FormatAlertBody tests ---

    [Fact]
    public void FormatAlertBody_ContainsAllSections() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("PQR.TO", "PQR Industries")],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Added: [MakeTopScoreCompanyRecord("GHI.TO", "GHI Ltd")],
            Removed: [MakeTopScoreCompanyRecord("PQR.TO", "PQR Industries")]);

        var body = TopScoreDiffComputer.FormatAlertBody(diff);

        _ = body.Should().Contain("Top-Score Companies Changed");
        _ = body.Should().Contain("New List (2 companies):");
        _ = body.Should().Contain("Previous List (2 companies):");
        _ = body.Should().Contain("Added (1):");
        _ = body.Should().Contain("Removed (1):");
        _ = body.Should().Contain("+ GHI.TO (GHI Ltd)");
        _ = body.Should().Contain("- PQR.TO (PQR Industries)");
        _ = body.Should().Contain("- ABC.TO (ABC Corp)");
    }

    [Fact]
    public void FormatAlertBody_SingleCompany_UsesSingularWord() {
        var diff = new TopScoreDiff(
            PreviousList: [],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            Added: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            Removed: []);

        var body = TopScoreDiffComputer.FormatAlertBody(diff);

        _ = body.Should().Contain("New List (1 company):");
        _ = body.Should().Contain("Previous List (0 companies):");
    }

    // --- FormatAlertBodyHtml tests ---

    [Fact]
    public void FormatAlertBodyHtml_ContainsHtmlStructure() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Added: [MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Removed: []);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        _ = html.Should().Contain("<!DOCTYPE html>");
        _ = html.Should().Contain("<html>");
        _ = html.Should().Contain("</html>");
        _ = html.Should().Contain("<table");
        _ = html.Should().Contain("</table>");
    }

    [Fact]
    public void FormatAlertBodyHtml_ContainsAllColumnHeaders() {
        var diff = new TopScoreDiff(
            PreviousList: [],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            Added: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            Removed: []);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        _ = html.Should().Contain("Symbol");
        _ = html.Should().Contain("Company Name");
        _ = html.Should().Contain("Price");
        _ = html.Should().Contain("Max Buy Price");
        _ = html.Should().Contain("% Upside");
        _ = html.Should().Contain("Market Cap");
        _ = html.Should().Contain("Est. Return (CF)");
        _ = html.Should().Contain("Est. Return (OE)");
        _ = html.Should().Contain("Score");
    }

    [Fact]
    public void FormatAlertBodyHtml_HighlightsAddedCompanies() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            NewList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp"), MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Added: [MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Removed: []);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        // Added company DEF.TO row should have score-15 teal background + added border
        _ = html.Should().Contain("background:#e1f2f3;");
        _ = html.Should().Contain("border-left:4px solid #2e7d32;");
        _ = html.Should().Contain("DEF.TO");
    }

    [Fact]
    public void FormatAlertBodyHtml_ContainsAddedAndRemovedSections() {
        var diff = new TopScoreDiff(
            PreviousList: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")],
            NewList: [MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Added: [MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc")],
            Removed: [MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp")]);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        _ = html.Should().Contain("Added");
        _ = html.Should().Contain("+ DEF.TO");
        _ = html.Should().Contain("Removed");
        _ = html.Should().Contain("- ABC.TO");
    }

    [Fact]
    public void FormatAlertBodyHtml_FormatsFinancialData() {
        var company = new TopScoreCompany("ABC.TO", "ABC Corp", 50M, 60M, 20M, 500_000_000M, 7M, 6M, 15);
        var diff = new TopScoreDiff(
            PreviousList: [],
            NewList: [company],
            Added: [company],
            Removed: []);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        // Should contain formatted currency and percentage values
        _ = html.Should().Contain("$50.00");
        _ = html.Should().Contain("$60.00");
        _ = html.Should().Contain("20.00%");
        _ = html.Should().Contain("7.00%");
        _ = html.Should().Contain("6.00%");
        _ = html.Should().Contain(">15<");
    }

    [Fact]
    public void FormatAlertBodyHtml_EscapesHtmlCharacters() {
        var company = new TopScoreCompany("ABC.TO", "A<B>&C Corp", 50M, 60M, 20M, 500_000_000M, 7M, 6M, 15);
        var diff = new TopScoreDiff(
            PreviousList: [],
            NewList: [company],
            Added: [company],
            Removed: []);

        var html = TopScoreDiffComputer.FormatAlertBodyHtml(diff);

        _ = html.Should().Contain("A&lt;B&gt;&amp;C Corp");
        _ = html.Should().NotContain("A<B>&C Corp");
    }

    // --- TopScoreCompany record tests ---

    [Fact]
    public void TopScoreCompany_CompareTo_SortsByInstrumentSymbol() {
        var a = MakeTopScoreCompanyRecord("AAA.TO", "AAA Corp");
        var z = MakeTopScoreCompanyRecord("ZZZ.TO", "ZZZ Corp");

        _ = a.CompareTo(z).Should().BeNegative();
        _ = z.CompareTo(a).Should().BePositive();
        _ = a.CompareTo(a).Should().Be(0);
    }

    [Fact]
    public void TopScoreCompany_CompareTo_Null_ReturnsPositive() {
        var a = MakeTopScoreCompanyRecord("AAA.TO", "AAA Corp");
        _ = a.CompareTo(null).Should().BePositive();
    }

    [Fact]
    public void TopScoreCompany_Equality_BasedOnSymbolOnly() {
        var a = new TopScoreCompany("ABC.TO", "ABC Corp", 50M, 60M, 20M, 500_000_000M, 7M, 6M, 15);
        var b = new TopScoreCompany("ABC.TO", "ABC Corp", 55M, 65M, 18M, 550_000_000M, 8M, 7M, 15);

        _ = a.Equals(b).Should().BeTrue();
        _ = a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void TopScoreCompany_Equality_DifferentSymbols_NotEqual() {
        var a = MakeTopScoreCompanyRecord("ABC.TO", "ABC Corp");
        var b = MakeTopScoreCompanyRecord("DEF.TO", "DEF Inc");

        _ = a.Equals(b).Should().BeFalse();
    }

    // --- Verify helper methods produce expected scores ---

    [Fact]
    public void MakeTopScoreCompany_Helper_ProducesTopScore() {
        var report = MakeTopScoreCompany("TEST.TO", "Test Corp");
        _ = report.OverallScore.Should().Be(15);
    }

    [Fact]
    public void MakeLowScoreCompany_Helper_ProducesScoreBelow13() {
        var report = MakeLowScoreCompany("TEST.TO", "Test Corp");
        _ = report.OverallScore.Should().BeLessThan(13);
    }
}
