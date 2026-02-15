using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace tsx_aggregator.models;

public record Score13Company(
    string InstrumentSymbol,
    string CompanyName,
    decimal PricePerShare,
    decimal MaxPrice,
    decimal PercentageUpside,
    decimal CurMarketCap,
    decimal EstReturnCashFlow,
    decimal EstReturnOwnerEarnings,
    int OverallScore) : IComparable<Score13Company> {

    public int CompareTo(Score13Company? other) {
        if (other is null)
            return 1;
        return string.Compare(InstrumentSymbol, other.InstrumentSymbol, StringComparison.OrdinalIgnoreCase);
    }

    // Equality based on InstrumentSymbol only, so diff comparison ignores changing financial data
    public virtual bool Equals(Score13Company? other) {
        if (other is null)
            return false;
        return string.Equals(InstrumentSymbol, other.InstrumentSymbol, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(InstrumentSymbol);
}

public record Score13Diff(
    IReadOnlyList<Score13Company> PreviousList,
    IReadOnlyList<Score13Company> NewList,
    IReadOnlyList<Score13Company> Added,
    IReadOnlyList<Score13Company> Removed);

public static class Score13DiffComputer {

    public static IReadOnlyList<Score13Company> ComputeScore13List(IReadOnlyList<CompanyFullDetailReport> reports) {
        return reports
            .Where(r => r.OverallScore == 13)
            .Select(r => new Score13Company(
                r.InstrumentSymbol,
                r.CompanyName,
                r.PricePerShare,
                r.MaxPrice,
                r.PercentageUpside,
                r.CurMarketCap,
                r.EstimatedNextYearTotalReturnPercentage_FromCashFlow,
                r.EstimatedNextYearTotalReturnPercentage_FromOwnerEarnings,
                r.OverallScore))
            .OrderBy(c => c)
            .ToList();
    }

    public static Score13Diff? ComputeDiff(IReadOnlyList<Score13Company> previous, IReadOnlyList<Score13Company> current) {
        var previousSet = new HashSet<Score13Company>(previous);
        var currentSet = new HashSet<Score13Company>(current);

        var added = current.Where(c => !previousSet.Contains(c)).OrderBy(c => c).ToList();
        var removed = previous.Where(c => !currentSet.Contains(c)).OrderBy(c => c).ToList();

        if (added.Count == 0 && removed.Count == 0)
            return null;

        return new Score13Diff(previous, current, added, removed);
    }

    public static string FormatAlertSubject(Score13Diff diff) {
        return $"TSX Score-13 Alert: {diff.Added.Count} added, {diff.Removed.Count} removed";
    }

    public static string FormatAlertBody(Score13Diff diff) {
        var sb = new StringBuilder();

        _ = sb.AppendLine("Score-13 Companies Changed")
              .AppendLine("==========================")
              .AppendLine()
              .AppendLine($"New List ({diff.NewList.Count} {(diff.NewList.Count == 1 ? "company" : "companies")}):");

        foreach (var company in diff.NewList)
            _ = sb.AppendLine($"  - {company.InstrumentSymbol} ({company.CompanyName})");

        _ = sb.AppendLine()
              .AppendLine($"Previous List ({diff.PreviousList.Count} {(diff.PreviousList.Count == 1 ? "company" : "companies")}):");

        foreach (var company in diff.PreviousList)
            _ = sb.AppendLine($"  - {company.InstrumentSymbol} ({company.CompanyName})");

        _ = sb.AppendLine()
              .AppendLine($"Added ({diff.Added.Count}):");

        foreach (var company in diff.Added)
            _ = sb.AppendLine($"  + {company.InstrumentSymbol} ({company.CompanyName})");

        _ = sb.AppendLine()
              .AppendLine($"Removed ({diff.Removed.Count}):");

        foreach (var company in diff.Removed)
            _ = sb.AppendLine($"  - {company.InstrumentSymbol} ({company.CompanyName})");

        return sb.ToString();
    }

    public static string FormatAlertBodyHtml(Score13Diff diff) {
        var addedSet = new HashSet<string>(diff.Added.Select(c => c.InstrumentSymbol), StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        _ = sb.AppendLine("<!DOCTYPE html>")
              .AppendLine("<html><head><meta charset=\"utf-8\"></head>")
              .AppendLine("<body style=\"font-family:Arial,Helvetica,sans-serif;color:#333;margin:0;padding:20px;\">");

        // Header
        _ = sb.AppendLine("<h2 style=\"color:#1a1a1a;margin-bottom:4px;\">Score-13 Companies Changed</h2>")
              .AppendLine($"<p style=\"font-size:16px;\"><strong>{diff.Added.Count}</strong> added, <strong>{diff.Removed.Count}</strong> removed</p>");

        // Added tickers
        if (diff.Added.Count > 0) {
            _ = sb.AppendLine("<h3 style=\"color:#2e7d32;\">Added</h3><ul style=\"list-style:none;padding-left:0;\">");
            foreach (var c in diff.Added)
                _ = sb.AppendLine($"<li style=\"color:#2e7d32;padding:2px 0;\">+ {Esc(c.InstrumentSymbol)} ({Esc(c.CompanyName)})</li>");
            _ = sb.AppendLine("</ul>");
        }

        // Removed tickers
        if (diff.Removed.Count > 0) {
            _ = sb.AppendLine("<h3 style=\"color:#c62828;\">Removed</h3><ul style=\"list-style:none;padding-left:0;\">");
            foreach (var c in diff.Removed)
                _ = sb.AppendLine($"<li style=\"color:#c62828;padding:2px 0;\">- {Esc(c.InstrumentSymbol)} ({Esc(c.CompanyName)})</li>");
            _ = sb.AppendLine("</ul>");
        }

        // Current list table (sorted by avg estimated return descending, matching top companies view)
        var sortedNewList = SortByAvgReturn(diff.NewList);
        _ = sb.AppendLine($"<h3>Current List ({diff.NewList.Count} {(diff.NewList.Count == 1 ? "company" : "companies")})</h3>");
        AppendCompanyTable(sb, sortedNewList, addedSet);

        // Previous list table
        var sortedPreviousList = SortByAvgReturn(diff.PreviousList);
        _ = sb.AppendLine($"<h3>Previous List ({diff.PreviousList.Count} {(diff.PreviousList.Count == 1 ? "company" : "companies")})</h3>");
        AppendCompanyTable(sb, sortedPreviousList, null);

        _ = sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static List<Score13Company> SortByAvgReturn(IReadOnlyList<Score13Company> companies) {
        var sorted = new List<Score13Company>(companies);
        sorted.Sort((a, b) => {
            int scoreCompare = b.OverallScore.CompareTo(a.OverallScore);
            if (scoreCompare != 0)
                return scoreCompare;
            decimal aAvg = (a.EstReturnCashFlow + a.EstReturnOwnerEarnings) / 2M;
            decimal bAvg = (b.EstReturnCashFlow + b.EstReturnOwnerEarnings) / 2M;
            return bAvg.CompareTo(aAvg);
        });
        return sorted;
    }

    private static void AppendCompanyTable(StringBuilder sb, IReadOnlyList<Score13Company> companies, HashSet<string>? highlightSymbols) {
        const string tableCss = "border-collapse:collapse;width:100%;font-size:14px;";
        const string thCss = "background:#f5f5f5;border:1px solid #ddd;padding:8px 12px;text-align:left;white-space:nowrap;";
        const string tdCss = "border:1px solid #ddd;padding:6px 12px;";
        const string tdRightCss = "border:1px solid #ddd;padding:6px 12px;text-align:right;";

        _ = sb.AppendLine($"<table style=\"{tableCss}\"><thead><tr>")
              .AppendLine($"<th style=\"{thCss}\">Symbol</th>")
              .AppendLine($"<th style=\"{thCss}\">Company Name</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Price</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Max Buy Price</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">% Upside</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Market Cap</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Est. Return (CF)</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Est. Return (OE)</th>")
              .AppendLine($"<th style=\"{thCss}text-align:right;\">Score</th>")
              .AppendLine("</tr></thead><tbody>");

        foreach (var c in companies) {
            bool highlight = highlightSymbols is not null && highlightSymbols.Contains(c.InstrumentSymbol);
            string rowStyle = highlight ? " style=\"background:#e8f5e9;\"" : "";

            _ = sb.AppendLine($"<tr{rowStyle}>")
                  .AppendLine($"<td style=\"{tdCss}font-weight:bold;\">{Esc(c.InstrumentSymbol)}</td>")
                  .AppendLine($"<td style=\"{tdCss}\">{Esc(c.CompanyName)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtCurrency(c.PricePerShare)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtCurrency(c.MaxPrice, naValue: -1)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtPercent(c.PercentageUpside)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtMarketCap(c.CurMarketCap)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtPercent(c.EstReturnCashFlow)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{FmtPercent(c.EstReturnOwnerEarnings)}</td>")
                  .AppendLine($"<td style=\"{tdRightCss}\">{c.OverallScore}</td>")
                  .AppendLine("</tr>");
        }

        _ = sb.AppendLine("</tbody></table>");
    }

    private static string Esc(string value) {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string FmtCurrency(decimal value, decimal naValue = decimal.MinValue) {
        if (value == naValue)
            return "N/A";
        return value.ToString("C2", CultureInfo.GetCultureInfo("en-CA"));
    }

    private static string FmtPercent(decimal value) {
        if (value == decimal.MinValue)
            return "N/A";
        return value.ToString("F2", CultureInfo.InvariantCulture) + "%";
    }

    private static string FmtMarketCap(decimal value) {
        if (value == decimal.MinValue || value == 0)
            return "N/A";
        return value.ToString("C0", CultureInfo.GetCultureInfo("en-CA"));
    }
}
