namespace tsx_aggregator.Services;

/// <summary>Tells the Aggregator to check its event queue now instead of at its next timeout (e.g. after a requeue).</summary>
internal interface IEventQueueWaker {
    void CheckForEventsNow();
}
