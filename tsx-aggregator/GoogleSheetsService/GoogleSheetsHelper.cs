using System;
using System.Collections.Generic;
using System.Linq;

namespace tsx_aggregator.Services;

public class GoogleSheetsHelper {
    public static string FindMaximumColumn(IEnumerable<string> columnNames) {
        // Convert each column name to its numerical equivalent and find the maximum value
        int maxColumnNumber = columnNames.Max(columnName => ConvertColumnNameToNumber(columnName));

        // Convert the maximum numerical value back to its column name equivalent
        string maxColumnName = ConvertNumberToColumnName(maxColumnNumber);

        return maxColumnName;
    }

    private static int ConvertColumnNameToNumber(string columnName) {
        int sum = 0;
        foreach (char c in columnName.ToUpperInvariant()) {
            sum *= 26;
            sum += c - 'A' + 1;
        }
        return sum;
    }

    private static string ConvertNumberToColumnName(int columnNumber) {
        string columnName = "";
        while (columnNumber > 0) {
            int modulo = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnName;
    }
}
