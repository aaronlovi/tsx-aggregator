using System;
using System.Collections.Generic;
using System.Text;

namespace tsx_aggregator.shared;

public abstract class LogUtils {
    public const string ReqIdContext = "ReqId";
    public const string ExchangeContext = "Exchange";
    public const string InstrumentSymbolContext = "InstrumentSymbol";
    public const string InstrumentReportIdContext = "InstrumentReportId";
    public const string InstrumentIdContext = "InstrumentId";

    private const int DefaultMaxItems = 5;

}
