using System;
using System.Threading;
using System.Threading.Tasks;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

/// <summary>The once-a-minute "still alive" log line of a long-running service.</summary>
internal static class Heartbeat {
    /// <summary>
    /// Calls <paramref name="beat"/> (which logs the service's heartbeat) now and then once a minute until
    /// <paramref name="ct"/> is cancelled. Services start it fire-and-forget from ExecuteAsync.
    /// </summary>
    public static async Task RunAsync(Action beat, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            beat();
            await Task.Delay(Constants.OneMinute, ct);
        }
    }
}
