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

    // A company that passes all 15 checks at $10/share with 100M shares (market cap $1B).
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
        AverageOwnerEarnings: 150_000_000);

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
    public void RowBuilder_AllChecksPass_ScoresFifteen() {
        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, AllPassFields);

        _ = row[Snapshot.ScoreColumn].Should().Be("15");
        _ = SnapshotRowBuilder.CheckColumns.Select(c => row[c]).Should().AllBe("P");
        _ = row[Snapshot.StatusColumn].Should().Be(SnapshotRowBuilder.StatusOk);
        _ = row["debt_to_equity"].Should().Be("0.1");
        _ = row.Keys.Should().BeEquivalentTo(SnapshotRowBuilder.Columns);
    }

    [Fact]
    public void RowBuilder_NegativeEquity_RecordsCurrentBehaviour() {
        // Documents bug B7 as it stands today: negative equity passes debt-to-equity (c01).
        ProcessedReportFields fields = AllPassFields with { CurTotalShareholdersEquity = -200_000_000 };

        Dictionary<string, string> row = SnapshotRowBuilder.Build(Instrument, fields);

        _ = row["c01"].Should().Be("P");
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
        Snapshot a = MakeSnapshot(("1", "AAA", 13), ("2", "BBB", 12), ("3", "CCC", 15), ("4", "DDD", 9));
        Snapshot b = MakeSnapshot(("1", "AAA", 12), ("2", "BBB", 14), ("3", "CCC", 15), ("5", "EEE", 13));

        SnapshotDiffResult diff = SnapshotDiff.Compute(a, b);

        _ = diff.CommonCount.Should().Be(3);
        _ = diff.OnlyInA.Should().Equal("DDD");
        _ = diff.OnlyInB.Should().Equal("EEE");
        _ = diff.ScoreChanges.Select(r => (r.Symbol, r.Delta)).Should().Equal(("BBB", 2), ("AAA", -1));
        _ = diff.DeltaHistogram.Should().Equal(new Dictionary<int, int> { [-1] = 1, [2] = 1 });

        ThresholdChange thirteen = diff.Thresholds.Single(t => t.Threshold == 13);
        _ = thirteen.Entered.Select(r => r.Symbol).Should().Equal("BBB");
        _ = thirteen.Left.Select(r => r.Symbol).Should().Equal("AAA");
        ThresholdChange fourteen = diff.Thresholds.Single(t => t.Threshold == 14);
        _ = fourteen.Entered.Select(r => r.Symbol).Should().Equal("BBB");
        _ = fourteen.Left.Should().BeEmpty();

        // MakeSnapshot passes the first <score> checks, so AAA flips c13 P→F and BBB flips c13, c14 F→P.
        _ = diff.CheckFlips["c13"].Should().Equal(new Dictionary<string, int> { ["F→P"] = 1, ["P→F"] = 1 });
        _ = diff.CheckFlips["c14"].Should().Equal(new Dictionary<string, int> { ["F→P"] = 1 });
        _ = diff.ColumnChanges[Snapshot.ScoreColumn].Should().Be(2);
    }

    [Fact]
    public void Diff_RowWithoutScore_IsOutsideEveryGroup() {
        Snapshot a = MakeSnapshot(("1", "AAA", 14));
        Snapshot b = MakeSnapshot(("1", "AAA", null));

        SnapshotDiffResult diff = SnapshotDiff.Compute(a, b);

        _ = diff.Thresholds.Single(t => t.Threshold == 13).Left.Select(r => r.Symbol).Should().Equal("AAA");
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
}
