using System;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;

namespace tsx_aggregator.Aggregated;

internal class AggregatorFsm {
    private readonly ApplicationCommonState _state;
    private readonly ILogger _logger;

    public AggregatorFsm(ILogger logger) {
        _state = new();
        _logger = logger;
    }

    public bool IsPaused {
        get => _state.IsPaused;
        set => _state.IsPaused = value;
    }

    public void Update(AggregatorInputBase input, AggregatorFsmOutputs output) {
        output.OutputList.Clear();

        switch (input) {
            case AggregatorTimeoutInput:
                if (IsPaused) {
                    _logger.LogWarning("AggregatorFsm received a timeout input while paused: {Input}", input);
                    break;
                }
                ProcessUpdateTime(output);
                break;
            case AggregatorPauseServiceInput pauseInput:
                ProcessPauseServiceInput(pauseInput, output);
                ProcessUpdateTime(output);
                break;
            default:
                _logger.LogWarning("AggregatorFsm received an unknown input: {Input}", input);
                break;
        }
    }

    private void ProcessUpdateTime(AggregatorFsmOutputs output) {
        if (IsPaused)
            return;

        output.OutputList.Add(new ProcessCheckForInstrumentEventOutput());
    }

    private void ProcessPauseServiceInput(AggregatorPauseServiceInput pauseInput, AggregatorFsmOutputs output) {
        bool pause = pauseInput.PauseNotResume;
        bool resume = !pause;
        bool isCommonServiceStateChanged = false;

        if (IsPaused && resume) {
            _logger.LogInformation("AggregatorFsm is resumed");
            IsPaused = false;
            isCommonServiceStateChanged = true;
        }
        else if (!IsPaused && pause) {
            _logger.LogInformation("AggregatorFsm is paused");
            IsPaused = true;
            isCommonServiceStateChanged = true;
        }

        if (isCommonServiceStateChanged)
            output.OutputList.Add(new PersistAggregatorCommonServiceStateOutput());
    }
}
