using System;
using tsx_aggregator.models;

namespace tsx_aggregator.Raw;

internal class StateFsm {
    private DateTime _curTime;
    private readonly Registry _registry;

    public StateFsm(DateTime curTime, Registry registry) {
        _curTime = curTime;
        _registry = registry;
        State = new();
    }

    public DateTime? NextFetchDirectoryTime {
        get => State.NextFetchDirectoryTime;
        set => State.NextFetchDirectoryTime = value;
    }

    public DateTime? NextFetchInstrumentDataTime {
        get => State.NextFetchInstrumentDataTime;
        set => State.NextFetchInstrumentDataTime = value;
    }

    public InstrumentKey PrevInstrumentKey {
        get => State.PrevInstrumentKey;
        set => State.PrevInstrumentKey = value;
    }

    public StateFsmState State { get; set; }

    public DateTime? NextTimeout => State.GetNextRawDataTimeout();

    public void Update(DateTime curTime, StateFsmOutputs output) {
        _curTime = curTime;
        output.OutputList.Clear();

        if (NextFetchDirectoryTime is null || _curTime > NextFetchDirectoryTime) {
            NextFetchDirectoryTime = _curTime.AddHours(1);
            output.OutputList.Add(new FetchDirectory());
        }

        if (NextFetchInstrumentDataTime is null || _curTime > NextFetchInstrumentDataTime) {
            NextFetchInstrumentDataTime = _curTime.AddMinutes(4);
            InstrumentKey? nextKey = _registry.GetNextInstrumentKey(PrevInstrumentKey);
            if (nextKey is not null) {
                PrevInstrumentKey = nextKey;
                output.OutputList.Add(new FetchInstrumentData(nextKey.CompanySymbol, nextKey.InstrumentSymbol, nextKey.Exchange));
            }
        }

        if (State.IsDirty)
            output.OutputList.Add(new PersistState());
    }
}
