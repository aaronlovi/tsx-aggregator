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
    public FetchInstrumentData(string companySymbol, string instrumentSymbol) : base() {
        CompanySymbol = companySymbol;
        InstrumentSymbol = instrumentSymbol;
    }

    public string CompanySymbol { get; init; }
    public string InstrumentSymbol { get; init; }
}

internal class PersistState : StateFsmOutputItemBase {
    public PersistState() : base() { }
}


internal class StateFsmOutputs {
    private List<StateFsmOutputItemBase> _outputList;

    public StateFsmOutputs() {
        _outputList = new();
    }

    public IList<StateFsmOutputItemBase> OutputList => _outputList;
}
