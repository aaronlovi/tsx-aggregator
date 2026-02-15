using System;

namespace tsx_aggregator.shared;

public record DateQuarter(int FullYear, int Quarter) {
    public static DateQuarter FromDate(DateTime d) {
        int year = d.Year;
        int month = d.Month;
        int quarter = (month <= 3) ? 1
            : (month <= 6) ? 2
            : (month <= 9) ? 3
            : 4;
        return new DateQuarter(year, quarter);
    }
}
