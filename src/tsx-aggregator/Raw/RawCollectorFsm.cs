using System;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;

namespace tsx_aggregator.Raw;

internal class RawCollectorFsm {
    private DateTime _curTime;
    private ApplicationCommonState _state;
    private readonly Registry _registry;
    private readonly ILogger _logger;

    public RawCollectorFsm(ILogger logger, DateTime curTime, Registry registry) {
        _curTime = curTime;
        _state = new();
        _registry = registry;
        _logger = logger;
    }

    public bool IsPaused {
        get => _state.IsPaused;
        set => _state.IsPaused = value;
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

    public void Update(RawCollectorInputBase input, DateTime utcNow, RawCollectorFsmOutputs output) {
        _curTime = utcNow;
        output.OutputList.Clear();

        switch (input) {
            case RawCollectorTimeoutInput:
                if (IsPaused) {
                    _logger.LogWarning("RawCollectorFsm received a timeout input while paused: {Input}", input);
                    break;
                }
                ProcessUpdateTime(output);
                break;
            case RawCollectorPauseServiceInput pauseInput:
                ProcessPauseServiceInput(pauseInput, output);
                ProcessUpdateTime(output);
                break;
            case RawCollectorIgnoreRawReportInput rawCollectorIgnoreRawReportInput:
                _logger.LogInformation("RawCollectorFsm received an ignore raw report input: {Input}", rawCollectorIgnoreRawReportInput);
                ProcessUpdateTime(output);
                break;
            case RawCollectorSetPriorityCompaniesInput:
            case RawCollectorGetPriorityCompaniesInput:
                ProcessUpdateTime(output);
                break;
            default:
                _logger.LogWarning("RawCollectorFsm received an unknown input: {Input}", input);
                break;
        }

        if (_state.IsDirty)
            output.OutputList.Add(new PersistRawCollectorFsmState());
    }

    private void ProcessPauseServiceInput(RawCollectorPauseServiceInput pauseInput, RawCollectorFsmOutputs output) {
        bool pause = pauseInput.PauseNotResume;
        bool resume = !pause;
        bool isCommonServiceStateChanged = false;

        if (IsPaused && resume) {
            _logger.LogInformation("RawCollectorFsm is resumed");
            IsPaused = false;
            isCommonServiceStateChanged = true;
        } else if (!IsPaused && pause) {
            _logger.LogInformation("RawCollectorFsm is paused");
            IsPaused = true;
            isCommonServiceStateChanged = true;
        }

        if (isCommonServiceStateChanged)
            output.OutputList.Add(new PersistRawCollectorCommonServiceState());
    }

    private void ProcessUpdateTime(RawCollectorFsmOutputs output) {
        if (IsPaused)
            return;

        if (NextFetchDirectoryTime is null || _curTime > NextFetchDirectoryTime) {
            NextFetchDirectoryTime = _curTime.AddHours(1);
            output.OutputList.Add(new FetchRawCollectorDirectoryOutput());
        }

        if (NextFetchInstrumentDataTime is null || _curTime > NextFetchInstrumentDataTime) {
            NextFetchInstrumentDataTime = _curTime.AddMinutes(4);

            // Check priority queue before round-robin
            if (_registry.TryDequeueNextPriorityInstrumentKey(out var priorityKey)) {
                // Use priority key but do NOT update PrevInstrumentKey so round-robin resumes from where it left off
                output.OutputList.Add(new FetchRawCollectorInstrumentDataOutput(priorityKey!.CompanySymbol, priorityKey.InstrumentSymbol, priorityKey.Exchange));
            } else {
                InstrumentKey? nextKey = _registry.GetNextInstrumentKey(PrevInstrumentKey);
                if (nextKey is not null) {
                    PrevInstrumentKey = nextKey;
                    output.OutputList.Add(new FetchRawCollectorInstrumentDataOutput(nextKey.CompanySymbol, nextKey.InstrumentSymbol, nextKey.Exchange));
                }
            }
        }
    }

    /// <summary>
    /// Used when state is restored from the database
    /// </summary>
    /// <remarks>
    /// TODO: Use a new constructor and the memento pattern instead
    /// </remarks>
    public void SetState(ApplicationCommonState state) => _state = state;

    /// <summary>
    /// Gets a copy of the current state.
    /// Expected to be used for persisting current state to the database
    /// </summary>
    public ApplicationCommonState GetCopyOfState() => new(_state);
}
