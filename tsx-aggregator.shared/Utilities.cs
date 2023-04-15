using System;

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
}
