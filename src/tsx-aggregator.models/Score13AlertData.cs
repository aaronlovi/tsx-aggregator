using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tsx_aggregator.models;

public record Score13Company(string InstrumentSymbol, string CompanyName) : IComparable<Score13Company> {
    public int CompareTo(Score13Company? other) {
        if (other is null)
            return 1;
        return string.Compare(InstrumentSymbol, other.InstrumentSymbol, StringComparison.OrdinalIgnoreCase);
    }
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
            .Select(r => new Score13Company(r.InstrumentSymbol, r.CompanyName))
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
}
