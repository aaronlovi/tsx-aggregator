using System;
using System.Threading;

namespace tsx_aggregator.shared;

public static class Utilities {
    public static decimal DivSafe(decimal numerator, decimal denominator, decimal defaultVal = decimal.MaxValue) {
        return denominator != 0 ? numerator / denominator : defaultVal;
    }

    public static bool Includes(this string str, string searchString) {
        return (str?.IndexOf(searchString, StringComparison.Ordinal) ?? -1) >= 0;
    }

    public static void SafeDispose(object? o) {
        if (o is IDisposable d)
            d.Dispose();
    }

    public static CancellationTokenSource CreateLinkedTokenSource(CancellationTokenSource? cts, CancellationToken ct) {
        return cts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
    }
}
