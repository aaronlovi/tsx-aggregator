using System.Collections.Generic;
using System.Threading;

namespace tsx_aggregator;

public sealed class RawCollectorSetPriorityCompaniesInput : RawCollectorInputBase {
    public RawCollectorSetPriorityCompaniesInput(
        long reqId, IReadOnlyList<string> companySymbols, CancellationTokenSource? cts)
        : base(reqId, cts) {
        CompanySymbols = companySymbols;
    }

    public IReadOnlyList<string> CompanySymbols { get; init; }
}

public sealed class RawCollectorGetPriorityCompaniesInput : RawCollectorInputBase {
    public RawCollectorGetPriorityCompaniesInput(long reqId, CancellationTokenSource? cts)
        : base(reqId, cts) { }
}
