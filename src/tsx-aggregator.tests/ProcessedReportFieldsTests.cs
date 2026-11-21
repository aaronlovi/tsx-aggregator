using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using tsx_aggregator.models;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

public class ProcessedReportFieldsTests {
    private const string CompleteJson = """
        {
          "CurNumShares": 1000,
          "CurTotalShareholdersEquity": 500.5,
          "CurGoodwill": 10.4,
          "CurIntangibles": 20.6,
          "CurLongTermDebt": 30,
          "CurDividendsPaid": 2.5,
          "CurAdjustedRetainedEarnings": 3.5,
          "OldestRetainedEarnings": -4.5,
          "CurBookValue": 470,
          "AverageNetCashFlow": 50,
          "AverageOwnerEarnings": 60
        }
        """;

    [Fact]
    public void TryParse_AllFieldsPresent_RoundsHalfToEven() {
        bool ok = ProcessedReportFields.TryParse(CompleteJson, out ProcessedReportFields? fields);

        _ = ok.Should().BeTrue();
        _ = fields.Should().Be(new ProcessedReportFields(
            CurNumShares: 1000,
            CurTotalShareholdersEquity: 500,
            CurGoodwill: 10,
            CurIntangibles: 21,
            CurLongTermDebt: 30,
            CurDividendsPaid: 2,
            CurAdjustedRetainedEarnings: 4,
            OldestRetainedEarnings: -4,
            CurBookValue: 470,
            AverageNetCashFlow: 50,
            AverageOwnerEarnings: 60,
            // A report written before B11 has no ROE inputs: the old formula, average / current equity.
            RoeCashFlowNumerator: 50,
            RoeCashFlowEquity: 500,
            RoeOwnerEarningsNumerator: 60,
            RoeOwnerEarningsEquity: 500));
    }

    [Fact]
    public void TryParse_RoeInputsPresent_ReadsThem() {
        string json = CompleteJson.Replace("\"AverageOwnerEarnings\": 60", """
            "AverageOwnerEarnings": 60,
            "RoeCashFlowNumerator": 300.4,
            "RoeCashFlowEquity": 2500,
            "RoeOwnerEarningsNumerator": null,
            "RoeOwnerEarningsEquity": null
            """);

        bool ok = ProcessedReportFields.TryParse(json, out ProcessedReportFields? fields);

        _ = ok.Should().BeTrue();
        _ = fields!.RoeCashFlowNumerator.Should().Be(300);
        _ = fields.RoeCashFlowEquity.Should().Be(2500);
        _ = fields.RoeOwnerEarningsNumerator.Should().BeNull();
        _ = fields.RoeOwnerEarningsEquity.Should().BeNull();
    }

    [Fact]
    public void TryParse_MissingField_ReturnsFalse() {
        string json = CompleteJson.Replace("\"AverageOwnerEarnings\": 60", "\"Other\": 60");

        bool ok = ProcessedReportFields.TryParse(json, out ProcessedReportFields? fields);

        _ = ok.Should().BeFalse();
        _ = fields.Should().BeNull();
    }

    [Fact]
    public void TryParse_NonNumericField_ReturnsFalse() {
        string json = CompleteJson.Replace("\"CurLongTermDebt\": 30", "\"CurLongTermDebt\": null");

        _ = ProcessedReportFields.TryParse(json, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_DecimalMinValueSentinel_Throws() {
        // decimal.MinValue does not fit in a long. Callers rely on this throwing so that they log and skip the company.
        string json = CompleteJson.Replace("\"AverageNetCashFlow\": 50", $"\"AverageNetCashFlow\": {decimal.MinValue}");

        Action act = () => ProcessedReportFields.TryParse(json, out _);

        _ = act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void TryParse_SerializedCompanyReport_ReadsItsProperties() {
        // Guards the field names against renames in CompanyReport.
        var report = new CompanyReport("ABC", "Abc Corp", "TSX", curNumShares: 1234);
        report.AddAnnualCashFlowStatement(new DateOnly(2025, 12, 31), new RawReportDataMap {
            ["NetIncomeFromContinuingOperations"] = 100,
            ["ChangesInCash"] = 40,
        });
        report.ProcessReports(new List<string>());
        report.CurTotalShareholdersEquity = 900;
        report.CurGoodwill = 50;
        report.CurIntangibles = 25;
        report.CurLongTermDebt = 300;
        report.CurDividendsPaid = 12;
        report.CurAdjustedRetainedEarnings = 77;
        report.OldestRetainedEarnings = 33;

        bool ok = ProcessedReportFields.TryParse(JsonSerializer.Serialize(report), out ProcessedReportFields? fields);

        _ = ok.Should().BeTrue();
        _ = fields!.CurNumShares.Should().Be(1234);
        _ = fields.CurTotalShareholdersEquity.Should().Be(900);
        _ = fields.CurGoodwill.Should().Be(50);
        _ = fields.CurIntangibles.Should().Be(25);
        _ = fields.CurLongTermDebt.Should().Be(300);
        _ = fields.CurDividendsPaid.Should().Be(12);
        _ = fields.CurAdjustedRetainedEarnings.Should().Be(77);
        _ = fields.OldestRetainedEarnings.Should().Be(33);
        _ = fields.CurBookValue.Should().Be(825);
        _ = fields.AverageNetCashFlow.Should().Be(40);
        _ = fields.AverageOwnerEarnings.Should().Be(100);
    }

    [Fact]
    public void TryParse_NullableFieldsNull_ReadsNulls() {
        string json = CompleteJson
            .Replace("\"CurTotalShareholdersEquity\": 500.5", "\"CurTotalShareholdersEquity\": null")
            .Replace("\"CurDividendsPaid\": 2.5", "\"CurDividendsPaid\": null")
            .Replace("\"CurAdjustedRetainedEarnings\": 3.5", "\"CurAdjustedRetainedEarnings\": null")
            .Replace("\"OldestRetainedEarnings\": -4.5", "\"OldestRetainedEarnings\": null")
            .Replace("\"CurBookValue\": 470", "\"CurBookValue\": null")
            .Replace("\"AverageNetCashFlow\": 50", "\"AverageNetCashFlow\": null")
            .Replace("\"AverageOwnerEarnings\": 60", "\"AverageOwnerEarnings\": null");

        bool ok = ProcessedReportFields.TryParse(json, out ProcessedReportFields? fields);

        _ = ok.Should().BeTrue();
        _ = fields.Should().Be(new ProcessedReportFields(
            CurNumShares: 1000,
            CurTotalShareholdersEquity: null,
            CurGoodwill: 10,
            CurIntangibles: 21,
            CurLongTermDebt: 30,
            CurDividendsPaid: null,
            CurAdjustedRetainedEarnings: null,
            OldestRetainedEarnings: null,
            CurBookValue: null,
            AverageNetCashFlow: null,
            AverageOwnerEarnings: null));
    }

    [Fact]
    public void TryParse_SerializedCompanyReportWithNoAnnualCashFlow_ReadsNullAverages() {
        // The old decimal.MinValue sentinel made this throw (and the company was skipped).
        var report = new CompanyReport("ABC", "Abc Corp", "TSX", curNumShares: 1234);
        report.ProcessReports(new List<string>());

        bool ok = ProcessedReportFields.TryParse(JsonSerializer.Serialize(report), out ProcessedReportFields? fields);

        _ = ok.Should().BeTrue();
        _ = fields!.AverageNetCashFlow.Should().BeNull();
        _ = fields.AverageOwnerEarnings.Should().BeNull();
        _ = fields.CurDividendsPaid.Should().BeNull();
        _ = fields.CurTotalShareholdersEquity.Should().BeNull();
        _ = fields.OldestRetainedEarnings.Should().BeNull();
    }

    [Fact]
    public void SerializedCompanyReport_HasNoUnusedComputedCopies() {
        // Ratios, est. book values and max price live only in CompanyFullDetailReport (decision 22).
        var report = new CompanyReport("ABC", "Abc Corp", "TSX", curNumShares: 1234);
        report.ProcessReports(new List<string>());

        string json = JsonSerializer.Serialize(report);

        _ = json.Should().NotContain("MaxPrice")
            .And.NotContain("EstimatedNextYearBookValue")
            .And.NotContain("DebtToEquityRatio")
            .And.NotContain("LongTermDebtToBookRatio")
            .And.NotContain("DidAdjustedRetainedEarningsIncrease");
    }

    [Fact]
    public void TryParse_ReportWithoutEstimateFlags_ReadsFalse() {
        // Reports written before decision 23 have no flags.
        _ = ProcessedReportFields.TryParse(CompleteJson, out ProcessedReportFields? fields).Should().BeTrue();
        _ = fields!.CurRetainedEarningsEstimated.Should().BeFalse();
        _ = fields.OldestRetainedEarningsEstimated.Should().BeFalse();
    }

    [Fact]
    public void TryParse_SerializedCompanyReport_ReadsEstimateFlags() {
        var report = new CompanyReport("ABC", "Abc Corp", "TSX", curNumShares: 1234);
        foreach ((DateOnly date, decimal? re) in new[] { (new DateOnly(2024, 12, 31), (decimal?)100m), (new DateOnly(2025, 12, 31), null) }) {
            report.AddAnnualCashFlowStatement(date, new RawReportDataMap { ["NetIncomeFromContinuingOperations"] = 50, ["CashDividendsPaid"] = 10 });
            report.AddAnnualIncomeStatement(date, new RawReportDataMap { ["NetIncomeContinuousOperations"] = 50 });
            var balanceSheet = new RawReportDataMap { ["StockholdersEquity"] = 500 };
            if (re is decimal r)
                balanceSheet["RetainedEarnings"] = r;
            report.AddAnnualBalanceSheet(date, balanceSheet);
        }
        report.ProcessReports(new List<string>());

        _ = ProcessedReportFields.TryParse(JsonSerializer.Serialize(report), out ProcessedReportFields? fields).Should().BeTrue();
        _ = fields!.CurRetainedEarningsEstimated.Should().BeTrue();
        _ = fields.OldestRetainedEarningsEstimated.Should().BeFalse();
    }
}
