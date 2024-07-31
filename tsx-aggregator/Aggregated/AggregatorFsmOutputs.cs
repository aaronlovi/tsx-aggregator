using System.Collections.Generic;

namespace tsx_aggregator.Aggregated;

internal abstract class AggregatorFsmOutputItemBase { }

internal class PersistAggregatorCommonServiceStateOutput : AggregatorFsmOutputItemBase {
    public PersistAggregatorCommonServiceStateOutput() : base() { }
}

internal class ProcessCheckForInstrumentEventOutput : AggregatorFsmOutputItemBase {
    public ProcessCheckForInstrumentEventOutput() : base() { }
}

internal class AggregatorFsmOutputs {
    private readonly List<AggregatorFsmOutputItemBase> _outputList;

    public AggregatorFsmOutputs() => _outputList = new();

    public IList<AggregatorFsmOutputItemBase> OutputList => _outputList;
}
