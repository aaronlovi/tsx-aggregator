using System;
using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

/// <summary>Posting a request to a background service and waiting for its result.</summary>
internal static class ServiceRequests {
    /// <summary>
    /// Builds a request with its own token source linked to <paramref name="ct"/>, posts it, and waits for its result,
    /// giving up (OperationCanceledException) when <paramref name="ct"/> is cancelled even if the service never answers.
    /// </summary>
    /// <returns>Posted is false, without waiting, when the service would not take the request.</returns>
    public static async Task<(bool Posted, object? Response)> PostAndAwaitAsync<TInput>(
        Func<CancellationTokenSource, TInput> makeInput, Func<TInput, bool> post, CancellationToken ct)
        where TInput : ServiceInputBase {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using TInput input = makeInput(cts);
        if (!post(input))
            return (false, null);
        return (true, await input.Completed.Task.WaitAsync(ct));
    }
}
