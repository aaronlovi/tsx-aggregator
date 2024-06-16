using System;
using tsx_aggregator.models;

namespace tsx_aggregator.Raw;

internal class StateFsm {
    private DateTime _curTime;
    private readonly Registry _registry;
    private StateFsmState _state;

    public StateFsm(DateTime curTime, Registry registry) {
        _curTime = curTime;
        _registry = registry;
        _state = new();
    }

    public DateTime? NextFetchDirectoryTime {
        get => _state.NextFetchDirectoryTime;
        set => _state.NextFetchDirectoryTime = value;
    }

    public DateTime? NextFetchInstrumentDataTime {
        get => _state.NextFetchInstrumentDataTime;
        set => _state.NextFetchInstrumentDataTime = value;
    }

    public InstrumentKey PrevInstrumentKey {
        get => _state.PrevInstrumentKey;
        set => _state.PrevInstrumentKey = value;
    }

    public DateTime? NextTimeout => _state.GetNextRawDataTimeout();

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

        if (_state.IsDirty)
            output.OutputList.Add(new PersistState());
    }

    /// <summary>
    /// Used when state is restored from the database
    /// </summary>
    /// <remarks>
    /// TODO: Use a new constructor and the memento pattern instead
    /// </remarks>
    public void SetState(StateFsmState state) => _state = state;

    /// <summary>
    /// Gets a copy of the current state.
    /// Expected to be used for persisting current state to the database
    /// </summary>
    public StateFsmState GetCopyOfState() => new(_state);
}
