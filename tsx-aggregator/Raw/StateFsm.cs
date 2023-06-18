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

    public CompanyAndInstrumentSymbol PrevCompanyAndInstrumentSymbol {
        get => State.PrevCompanyAndInstrumentSymbol;
        set => State.PrevCompanyAndInstrumentSymbol = value;
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
            CompanyAndInstrumentSymbol? nextCompanyAndInstrumentSymbol_ = _registry.GetNextCompanyAndInstrumentSymbol(PrevCompanyAndInstrumentSymbol);
            if (nextCompanyAndInstrumentSymbol_ is not null) {
                PrevCompanyAndInstrumentSymbol = nextCompanyAndInstrumentSymbol_;
                output.OutputList.Add(new FetchInstrumentData(nextCompanyAndInstrumentSymbol_.CompanySymbol, nextCompanyAndInstrumentSymbol_.InstrumentSymbol));
            }
        }

        if (State.IsDirty)
            output.OutputList.Add(new PersistState());
    }
}
