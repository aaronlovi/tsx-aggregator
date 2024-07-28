using System.Collections.Generic;

namespace tsx_aggregator;

internal abstract class RawCollectorFsmOutputItemBase { }

internal class FetchRawCollectorDirectoryOutput : RawCollectorFsmOutputItemBase {
    public FetchRawCollectorDirectoryOutput() : base() { }
}

internal class FetchRawCollectorInstrumentDataOutput : RawCollectorFsmOutputItemBase {
    public FetchRawCollectorInstrumentDataOutput(string companySymbol, string instrumentSymbol, string exchange) : base() {
        CompanySymbol = companySymbol;
        InstrumentSymbol = instrumentSymbol;
        Exchange = exchange;
    }

    public string CompanySymbol { get; init; }
    public string InstrumentSymbol { get; init; }
    public string Exchange { get; init; }
}

internal class PersistRawCollectorFsmState : RawCollectorFsmOutputItemBase {
    public PersistRawCollectorFsmState() : base() { }
}

internal class PersistRawCollectorCommonServiceState : RawCollectorFsmOutputItemBase {
    public PersistRawCollectorCommonServiceState() : base() { }
}

internal class RawCollectorFsmOutputs {
    private readonly List<RawCollectorFsmOutputItemBase> _outputList;

    public RawCollectorFsmOutputs() => _outputList = new();

    public IList<RawCollectorFsmOutputItemBase> OutputList => _outputList;
}
