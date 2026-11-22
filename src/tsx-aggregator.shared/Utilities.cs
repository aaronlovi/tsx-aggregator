using System;

namespace tsx_aggregator.shared;

public static class Utilities {

    /// <summary>
    /// Divides, returning null when either operand is null or the denominator is zero.
    /// </summary>
    public static decimal? DivOrNull(decimal? numerator, decimal? denominator)
        => numerator is decimal n && denominator is decimal d && d != 0 ? n / d : null;

    /// <summary>
    /// Determines whether the specified string contains the specified search string.
    /// </summary>
    /// <param name="str">The string to search within. If this string is null, the method returns false.</param>
    /// <param name="searchString">The string to search for.</param>
    /// <returns>true if the search string is found in the string; otherwise, false.</returns>
    public static bool Includes(this string str, string searchString)
        => (str?.IndexOf(searchString, StringComparison.Ordinal) ?? -1) >= 0;

    /// <summary>
    /// Calculates the time difference between two DateTime objects.
    /// </summary>
    /// <param name="startTime">The start time of the interval.</param>
    /// <param name="endTime">The end time of the interval. If null, the start time is used as the end time.</param>
    /// <returns>The time difference as a TimeSpan. If the calculated difference is less than or equal to zero, a TimeSpan representing one millisecond is returned.</returns>
    public static TimeSpan CalculateTimeDifference(DateTime startTime, DateTime? endTime) {
        TimeSpan difference = endTime.GetValueOrDefault(startTime) - startTime;
        return difference > TimeSpan.Zero ? difference : TimeSpan.FromMilliseconds(1);
    }

    public static bool EqualsInvariant(this string str, string other)
        => str.Equals(other, StringComparison.InvariantCulture);
}
