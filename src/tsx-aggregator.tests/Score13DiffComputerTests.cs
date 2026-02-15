using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class Score13DiffComputerTests {

    /// <summary>
    /// Creates a CompanyFullDetailReport that passes all 13 checks (OverallScore == 13).
    /// </summary>
    private static CompanyFullDetailReport MakeScore13Company(string symbol, string name) {
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
            averageNetCashFlow: 40_000_000M,
            averageOwnerEarnings: 35_000_000M,
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

    // --- ComputeScore13List tests ---

    [Fact]
    public void ComputeScore13List_WithMixedScores_ReturnsOnlyScore13Companies() {
        // Arrange
        var reports = new List<CompanyFullDetailReport> {
            MakeScore13Company("ABC.TO", "ABC Corp"),
            MakeLowScoreCompany("DEF.TO", "DEF Inc"),
            MakeScore13Company("GHI.TO", "GHI Ltd"),
            MakeLowScoreCompany("JKL.TO", "JKL Holdings")
        };

        // Act
        var result = Score13DiffComputer.ComputeScore13List(reports);

        // Assert
        _ = result.Should().HaveCount(2);
        _ = result[0].InstrumentSymbol.Should().Be("ABC.TO");
        _ = result[1].InstrumentSymbol.Should().Be("GHI.TO");
    }

    [Fact]
    public void ComputeScore13List_WithEmptyInput_ReturnsEmptyList() {
        var result = Score13DiffComputer.ComputeScore13List([]);
        _ = result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeScore13List_WithNoScore13Companies_ReturnsEmptyList() {
        var reports = new List<CompanyFullDetailReport> {
            MakeLowScoreCompany("ABC.TO", "ABC Corp"),
            MakeLowScoreCompany("DEF.TO", "DEF Inc")
        };

        var result = Score13DiffComputer.ComputeScore13List(reports);
        _ = result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeScore13List_ReturnsSortedBySymbol() {
        var reports = new List<CompanyFullDetailReport> {
            MakeScore13Company("ZZZ.TO", "ZZZ Corp"),
            MakeScore13Company("AAA.TO", "AAA Inc"),
            MakeScore13Company("MMM.TO", "MMM Ltd")
        };

        var result = Score13DiffComputer.ComputeScore13List(reports);
        _ = result.Should().HaveCount(3);
        _ = result[0].InstrumentSymbol.Should().Be("AAA.TO");
        _ = result[1].InstrumentSymbol.Should().Be("MMM.TO");
        _ = result[2].InstrumentSymbol.Should().Be("ZZZ.TO");
    }

    [Fact]
    public void ComputeScore13List_CompanyWithZeroPrice_IsExcluded() {
        // A company with PricePerShare=0 can't have OverallScore=13
        var report = MakeLowScoreCompany("ABC.TO", "ABC Corp");
        _ = report.OverallScore.Should().BeLessThan(13, "a company with no price data cannot pass all 13 checks");

        var result = Score13DiffComputer.ComputeScore13List([report]);
        _ = result.Should().BeEmpty();
    }

    // --- ComputeDiff tests ---

    [Fact]
    public void ComputeDiff_IdenticalLists_ReturnsNull() {
        var list = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc")
        };

        var result = Score13DiffComputer.ComputeDiff(list, list);
        _ = result.Should().BeNull();
    }

    [Fact]
    public void ComputeDiff_AdditionsOnly_ReturnsCorrectDiff() {
        var previous = new List<Score13Company> {
            new("ABC.TO", "ABC Corp")
        };
        var current = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc"),
            new("GHI.TO", "GHI Ltd")
        };

        var result = Score13DiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(2);
        _ = result.Added[0].InstrumentSymbol.Should().Be("DEF.TO");
        _ = result.Added[1].InstrumentSymbol.Should().Be("GHI.TO");
        _ = result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_RemovalsOnly_ReturnsCorrectDiff() {
        var previous = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc"),
            new("GHI.TO", "GHI Ltd")
        };
        var current = new List<Score13Company> {
            new("ABC.TO", "ABC Corp")
        };

        var result = Score13DiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().BeEmpty();
        _ = result.Removed.Should().HaveCount(2);
        _ = result.Removed[0].InstrumentSymbol.Should().Be("DEF.TO");
        _ = result.Removed[1].InstrumentSymbol.Should().Be("GHI.TO");
    }

    [Fact]
    public void ComputeDiff_BothAdditionsAndRemovals_ReturnsCorrectDiff() {
        var previous = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc")
        };
        var current = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("GHI.TO", "GHI Ltd")
        };

        var result = Score13DiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(1);
        _ = result.Added[0].InstrumentSymbol.Should().Be("GHI.TO");
        _ = result.Removed.Should().HaveCount(1);
        _ = result.Removed[0].InstrumentSymbol.Should().Be("DEF.TO");
    }

    [Fact]
    public void ComputeDiff_FromEmptyToNonEmpty_AllItemsInAdded() {
        var previous = new List<Score13Company>();
        var current = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc")
        };

        var result = Score13DiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().HaveCount(2);
        _ = result.Removed.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDiff_FromNonEmptyToEmpty_AllItemsInRemoved() {
        var previous = new List<Score13Company> {
            new("ABC.TO", "ABC Corp"),
            new("DEF.TO", "DEF Inc")
        };
        var current = new List<Score13Company>();

        var result = Score13DiffComputer.ComputeDiff(previous, current);

        _ = result.Should().NotBeNull();
        _ = result!.Added.Should().BeEmpty();
        _ = result.Removed.Should().HaveCount(2);
    }

    // --- FormatAlertSubject tests ---

    [Fact]
    public void FormatAlertSubject_WithKnownDiff_ReturnsCorrectSubject() {
        var diff = new Score13Diff(
            PreviousList: [new("ABC.TO", "ABC Corp")],
            NewList: [new("ABC.TO", "ABC Corp"), new("DEF.TO", "DEF Inc"), new("GHI.TO", "GHI Ltd")],
            Added: [new("DEF.TO", "DEF Inc"), new("GHI.TO", "GHI Ltd")],
            Removed: []);

        var subject = Score13DiffComputer.FormatAlertSubject(diff);
        _ = subject.Should().Be("TSX Score-13 Alert: 2 added, 0 removed");
    }

    [Fact]
    public void FormatAlertSubject_WithAdditionsAndRemovals_ReturnsCorrectSubject() {
        var diff = new Score13Diff(
            PreviousList: [new("ABC.TO", "ABC Corp"), new("PQR.TO", "PQR Industries")],
            NewList: [new("ABC.TO", "ABC Corp"), new("GHI.TO", "GHI Ltd")],
            Added: [new("GHI.TO", "GHI Ltd")],
            Removed: [new("PQR.TO", "PQR Industries")]);

        var subject = Score13DiffComputer.FormatAlertSubject(diff);
        _ = subject.Should().Be("TSX Score-13 Alert: 1 added, 1 removed");
    }

    // --- FormatAlertBody tests ---

    [Fact]
    public void FormatAlertBody_ContainsAllSections() {
        var diff = new Score13Diff(
            PreviousList: [new("ABC.TO", "ABC Corp"), new("PQR.TO", "PQR Industries")],
            NewList: [new("ABC.TO", "ABC Corp"), new("GHI.TO", "GHI Ltd")],
            Added: [new("GHI.TO", "GHI Ltd")],
            Removed: [new("PQR.TO", "PQR Industries")]);

        var body = Score13DiffComputer.FormatAlertBody(diff);

        _ = body.Should().Contain("Score-13 Companies Changed");
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
        var diff = new Score13Diff(
            PreviousList: [],
            NewList: [new("ABC.TO", "ABC Corp")],
            Added: [new("ABC.TO", "ABC Corp")],
            Removed: []);

        var body = Score13DiffComputer.FormatAlertBody(diff);

        _ = body.Should().Contain("New List (1 company):");
        _ = body.Should().Contain("Previous List (0 companies):");
    }

    // --- Score13Company record tests ---

    [Fact]
    public void Score13Company_CompareTo_SortsByInstrumentSymbol() {
        var a = new Score13Company("AAA.TO", "AAA Corp");
        var z = new Score13Company("ZZZ.TO", "ZZZ Corp");

        _ = a.CompareTo(z).Should().BeNegative();
        _ = z.CompareTo(a).Should().BePositive();
        _ = a.CompareTo(a).Should().Be(0);
    }

    [Fact]
    public void Score13Company_CompareTo_Null_ReturnsPositive() {
        var a = new Score13Company("AAA.TO", "AAA Corp");
        _ = a.CompareTo(null).Should().BePositive();
    }

    // --- Verify helper methods produce expected scores ---

    [Fact]
    public void MakeScore13Company_Helper_ProducesScore13() {
        var report = MakeScore13Company("TEST.TO", "Test Corp");
        _ = report.OverallScore.Should().Be(13);
    }

    [Fact]
    public void MakeLowScoreCompany_Helper_ProducesScoreBelow13() {
        var report = MakeLowScoreCompany("TEST.TO", "Test Corp");
        _ = report.OverallScore.Should().BeLessThan(13);
    }
}
