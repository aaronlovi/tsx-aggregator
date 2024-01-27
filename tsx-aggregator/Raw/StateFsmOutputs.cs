using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tsx_aggregator;

internal abstract class StateFsmOutputItemBase { }

internal class FetchDirectory : StateFsmOutputItemBase {
    public FetchDirectory() : base() { }
}

internal class FetchInstrumentData : StateFsmOutputItemBase {
    public FetchInstrumentData(string companySymbol, string instrumentSymbol, string exchange) : base() {
        CompanySymbol = companySymbol;
        InstrumentSymbol = instrumentSymbol;
        Exchange = exchange;
    }

    public string CompanySymbol { get; init; }
    public string InstrumentSymbol { get; init; }
    public string Exchange { get; init; }
}

internal class PersistState : StateFsmOutputItemBase {
    public PersistState() : base() { }
}


internal class StateFsmOutputs {
    private readonly List<StateFsmOutputItemBase> _outputList;

    public StateFsmOutputs() {
        _outputList = new();
    }

    public IList<StateFsmOutputItemBase> OutputList => _outputList;
}
