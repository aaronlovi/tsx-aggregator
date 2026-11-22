using System;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.Logging;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>The pause plumbing shared by the <see cref="IPausableService"/> implementations.</summary>
internal static class PausableService {
    /// <summary>
    /// <see cref="IPausableService.SetPausedAsync"/>: posts the service's pause/resume request and waits until it is done
    /// (the new state saved), or until <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async Task SetPausedAsync<TInput>(
        string serviceName, Func<CancellationTokenSource, TInput> makeInput, Func<TInput, bool> post, CancellationToken ct)
        where TInput : ServiceInputBase {
        (bool posted, _) = await ServiceRequests.PostAndAwaitAsync(makeInput, post, ct);
        if (!posted)
            throw new InvalidOperationException($"{serviceName} - failed to post the pause/resume request");
    }

    /// <summary>
    /// Saves the paused state in service_state, then completes the pause/resume request: with null when saved, or with an
    /// InvalidOperationException carrying the error when not.
    /// </summary>
    public static async Task PersistPausedStateAsync(
        IDbmService dbm, ILogger logger, string serviceName, bool isPaused, ServiceInputBase input, CancellationToken ct) {
        logger.LogInformation("ProcessPersistCommonServiceState begin");
        Result res = await dbm.PersistCommonServiceState(isPaused, serviceName, ct);
        if (res.Success) {
            logger.LogInformation("ProcessPersistCommonServiceState success");
            _ = input.Completed.TrySetResult(null);
        } else {
            logger.LogInformation("ProcessPersistCommonServiceState failed with error: {ErrMsg}", res.ErrMsg);
            _ = input.Completed.TrySetException(new InvalidOperationException(res.ErrMsg));
        }
    }
}
