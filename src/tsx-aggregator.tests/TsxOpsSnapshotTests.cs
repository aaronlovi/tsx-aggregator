using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_ops;
using tsx_ops.Commands;
using tsx_ops.Snapshots;

namespace tsx_aggregator.tests;

public class TsxOpsSnapshotTests {
    private static readonly SnapshotInstrument Instrument = new(
        InstrumentId: 42,
        Exchange: "TSX",
        CompanySymbol: "ABC",
        CompanyName: "Abc, \"The\" Corp",
        InstrumentSymbol: "ABC",
        InstrumentName: "Abc Corp",
        PricePerShare: 10M,
        NumShares: 100_000_000,
        NumAnnualCashFlowReports: 5);

    // A company that passes all 16 checks at $10/share with 100M shares (market cap $1B).
    private static readonly ProcessedReportFields AllPassFields = new(
        CurNumShares: 100_000_000,
        CurTotalShareholdersEquity: 1_000_000_000,
        CurGoodwill: 0,
        CurIntangibles: 0,
        CurLongTermDebt: 100_000_000,
        CurDividendsPaid: 0,
        CurAdjustedRetainedEarnings: 500_000_000,
        OldestRetainedEarnings: 100_000_000,
        CurBookValue: 1_000_000_000,
        AverageNetCashFlow: 150_000_000,
        AverageOwnerEarnings: 150_000_000,
        RoeCashFlowNumerator: 750_000_000,
        RoeCashFlowEquity: 5_000_000_000,
        RoeOwnerEarningsNumerator: 750_000_000,
        RoeOwnerEarningsEquity: 5_000_000_000,
        RoeOwnerEarningsByYear: TestDataFactory.ConsistentRoeYears());

    [Fact]
    public void Csv_RoundTripsQuotesCommasAndEmptyFields() {
        string[] fields = ["plain", "with, comma", "with \"quotes\"", "", "line\nbreak"];
        string line = Csv.FormatLine(fields) + "\n" + Csv.FormatLine(["a", "b", "c", "d", "e"]) + "\n";

        List<string[]> rows = Csv.ReadAll(new StringReader(line));

        _ = rows.Should().HaveCount(2);
        _ = rows[0].Should().Equal(fields);
        _ = rows[1].Should().Equal("a", "b", "c", "d", "e");
    }

    [Fact]
    public void RowBuilder_AllChecksPass_ScoresSixteen() {
        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, AllPassFields, new ScoringOptions());

        _ = row[Snapshot.ScoreColumn].Should().Be("16");
        _ = SnapshotRowBuilder.CheckColumns.Select(c => row[c]).Should().AllBe("P");
        _ = row[Snapshot.StatusColumn].Should().Be(SnapshotRowBuilder.StatusOk);
        _ = row["debt_to_equity"].Should().Be("0.1");
        _ = row.Keys.Should().BeEquivalentTo(SnapshotRowBuilder.Columns);
    }

    [Fact]
    public void RowBuilder_UsesTheGivenScoringSettings() {
        // ROE (OE) over 10% in 3 of the last 5 years: check 16 (c16) fails with the default 4 of 5, passes with 3 of 5.
        ProcessedReportFields fields = AllPassFields with {
            RoeOwnerEarningsByYear = [
                new AnnualReturnOnEquity(new DateOnly(2025, 12, 31), 0.2m),
                new AnnualReturnOnEquity(new DateOnly(2024, 12, 31), 0.05m),
                new AnnualReturnOnEquity(new DateOnly(2023, 12, 31), 0.2m),
                new AnnualReturnOnEquity(new DateOnly(2022, 12, 31), 0.05m),
                new AnnualReturnOnEquity(new DateOnly(2021, 12, 31), 0.2m)],
        };

        _ = SnapshotRowBuilder.Build(Instrument, fields, new ScoringOptions())["c16"].Should().Be("F");
        _ = SnapshotRowBuilder.Build(Instrument, fields, new ScoringOptions { RoeConsistencyMinYears = 3 })["c16"].Should().Be("P");
    }

    [Fact]
    public void RowBuilder_NegativeEquity_FailsDebtToEquity() {
        // B7 sign rule: negative equity fails debt-to-equity (c01) instead of passing with a negative ratio.
        ProcessedReportFields fields = AllPassFields with { CurTotalShareholdersEquity = -200_000_000 };

        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, fields, new ScoringOptions());

        _ = row["c01"].Should().Be("F");
    }

    [Fact]
    public void RowBuilder_MissingEquity_WritesNotAvailableAndEmptyCells() {
        // W1: no equity (so no book value, and no ROE equity) makes checks 1, 2, 3, 10, 14 and 15 N; the missing
        // figures are empty cells.
        ProcessedReportFields fields = AllPassFields with {
            CurTotalShareholdersEquity = null, CurBookValue = null, RoeCashFlowEquity = null, RoeOwnerEarningsEquity = null };

        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, fields, new ScoringOptions());

        _ = row["cur_total_shareholders_equity"].Should().BeEmpty();
        _ = row["cur_book_value"].Should().BeEmpty();
        _ = row["debt_to_equity"].Should().BeEmpty();
        _ = row["max_price"].Should().BeEmpty();
        _ = new[] { "c01", "c02", "c03", "c10", "c14", "c15" }.Select(c => row[c]).Should().AllBe("N");
        _ = row["na_count"].Should().Be("6");
        _ = row["c04"].Should().Be("P");
    }

    [Fact]
    public void Snapshot_WriteThenRead_PreservesRows() {
        Snapshot original = SnapshotBuilder.FromProcessed([Instrument], new Dictionary<long, string>());
        var writer = new StringWriter();
        original.Write(writer);

        Snapshot read = Snapshot.Read(new StringReader(writer.ToString()));

        _ = read.Columns.Should().Equal(original.Columns);
        _ = read.Rows.Should().HaveCount(1);
        _ = read.Rows[0]["company_name"].Should().Be(Instrument.CompanyName);
        _ = read.Rows[0][Snapshot.StatusColumn].Should().Be(SnapshotBuilder.StatusNoProcessedReport);
    }

    [Fact]
    public void SnapshotBuilder_FromProcessed_RecordsUnreadableReportsInsteadOfDroppingThem() {
        SnapshotInstrument missingField = Instrument with { InstrumentId = 1, InstrumentSymbol = "AAA" };
        SnapshotInstrument overflow = Instrument with { InstrumentId = 2, InstrumentSymbol = "BBB" };
        SnapshotInstrument noReport = Instrument with { InstrumentId = 3, InstrumentSymbol = "CCC" };
        var processed = new Dictionary<long, string> {
            [1] = "{\"CurNumShares\": 1}",
            [2] = SerializedFields(AllPassFields).Replace("\"AverageNetCashFlow\":150000000", $"\"AverageNetCashFlow\":{decimal.MinValue}"),
        };

        Snapshot snapshot = SnapshotBuilder.FromProcessed([overflow, noReport, missingField], processed);

        _ = snapshot.Rows.Select(r => r[Snapshot.SymbolColumn]).Should().Equal("AAA", "BBB", "CCC");
        _ = snapshot.Rows.Select(r => r[Snapshot.StatusColumn]).Should().Equal(
            SnapshotRowBuilder.StatusInvalid, "error:OverflowException", SnapshotBuilder.StatusNoProcessedReport);
        _ = snapshot.Rows.Should().OnlyContain(r => r[Snapshot.ScoreColumn] == string.Empty);
    }

    [Fact]
    public void Diff_IdenticalSnapshots_HasNoChanges() {
        Snapshot a = MakeSnapshot(("1", "AAA", 13), ("2", "BBB", 10));

        SnapshotDiffResult diff = SnapshotDiff.Compute(a, a);

        _ = diff.HasNoChanges.Should().BeTrue();
        _ = SnapshotDiff.RenderMarkdown(diff, "a", "b", 20).Should().Contain("No differences");
    }

    [Fact]
    public void Diff_ReportsGroupEntryExitFlipsAndMembershipChanges() {
        Snapshot a = MakeSnapshot(("1", "AAA", 14), ("2", "BBB", 13), ("3", "CCC", 16), ("4", "DDD", 10));
        Snapshot b = MakeSnapshot(("1", "AAA", 13), ("2", "BBB", 15), ("3", "CCC", 16), ("5", "EEE", 14));

        SnapshotDiffResult diff = SnapshotDiff.Compute(a, b);

        _ = diff.CommonCount.Should().Be(3);
        _ = diff.OnlyInA.Should().Equal("DDD");
        _ = diff.OnlyInB.Should().Equal("EEE");
        _ = diff.ScoreChanges.Select(r => (r.Symbol, r.Delta)).Should().Equal(("BBB", 2), ("AAA", -1));
        _ = diff.DeltaHistogram.Should().Equal(new Dictionary<int, int> { [-1] = 1, [2] = 1 });

        // Groups since C16: 14+ (the cutoff), 15+ and 16/16.
        _ = diff.Thresholds.Select(t => t.Threshold).Should().Equal(14, 15, 16);
        ThresholdChange fourteen = diff.Thresholds.Single(t => t.Threshold == 14);
        _ = fourteen.Entered.Select(r => r.Symbol).Should().Equal("BBB");
        _ = fourteen.Left.Select(r => r.Symbol).Should().Equal("AAA");
        ThresholdChange fifteen = diff.Thresholds.Single(t => t.Threshold == 15);
        _ = fifteen.Entered.Select(r => r.Symbol).Should().Equal("BBB");
        _ = fifteen.Left.Should().BeEmpty();

        // MakeSnapshot passes the first <score> checks, so AAA flips c14 P→F and BBB flips c14, c15 F→P.
        _ = diff.CheckFlips["c14"].Should().Equal(new Dictionary<string, int> { ["F→P"] = 1, ["P→F"] = 1 });
        _ = diff.CheckFlips["c15"].Should().Equal(new Dictionary<string, int> { ["F→P"] = 1 });
        _ = diff.ColumnChanges[Snapshot.ScoreColumn].Should().Be(2);
    }

    [Fact]
    public void Diff_RowWithoutScore_IsOutsideEveryGroup() {
        Snapshot a = MakeSnapshot(("1", "AAA", 14));
        Snapshot b = MakeSnapshot(("1", "AAA", null));

        SnapshotDiffResult diff = SnapshotDiff.Compute(a, b);

        _ = diff.Thresholds.Single(t => t.Threshold == 14).Left.Select(r => r.Symbol).Should().Equal("AAA");
        _ = diff.StatusChanges.Should().ContainSingle().Which.Should().Be(("AAA", "ok", "error:OverflowException"));
        _ = diff.CheckFlips["c01"].Should().Equal(new Dictionary<string, int> { ["P→-"] = 1 });
    }

    [Fact]
    public void ParsedArgs_ParsesOptionsFlagsAndPositionals() {
        ParsedArgs parsed = ParsedArgs.Parse(["a.csv", "--top", "5", "--execute", "b.csv"], ["--top"], ["--execute"]);

        _ = parsed.Positionals.Should().Equal("a.csv", "b.csv");
        _ = parsed.GetInt("--top", 20).Should().Be(5);
        _ = parsed.Has("--execute").Should().BeTrue();
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--top")]
    public void ParsedArgs_BadOption_ThrowsUsageException(string arg) {
        Action act = () => ParsedArgs.Parse([arg], ["--top"], []);

        _ = act.Should().Throw<UsageException>();
    }

    private static string SerializedFields(ProcessedReportFields f) =>
        System.Text.Json.JsonSerializer.Serialize(f);

    /// <summary>Rows whose first <c>score</c> checks pass; a null score means an unreadable report.</summary>
    private static Snapshot MakeSnapshot(params (string Id, string Symbol, int? Score)[] rows) {
        var built = new List<IReadOnlyDictionary<string, string>>();
        foreach ((string id, string symbol, int? score) in rows) {
            var row = SnapshotRowBuilder.Columns.ToDictionary(c => c, _ => "0", StringComparer.Ordinal);
            row[Snapshot.InstrumentIdColumn] = id;
            row[Snapshot.SymbolColumn] = symbol;
            row["company_name"] = symbol;
            row[Snapshot.StatusColumn] = score is null ? "error:OverflowException" : SnapshotRowBuilder.StatusOk;
            row[Snapshot.ScoreColumn] = score?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            for (int i = 0; i < SnapshotRowBuilder.NumChecks; i++)
                row[SnapshotRowBuilder.CheckColumns[i]] = score is null ? string.Empty : i < score ? "P" : "F";
            built.Add(row);
        }

        return new Snapshot(SnapshotRowBuilder.Columns, built);
    }

    [Fact]
    public void RowBuilder_HasTheReportingCurrency() {
        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, AllPassFields with { ReportingCurrency = "USD" }, new ScoringOptions());

        _ = row["reporting_currency"].Should().Be("USD");
        _ = SnapshotRowBuilder.Build(Instrument, AllPassFields, new ScoringOptions())["reporting_currency"].Should().BeEmpty();
    }
}
