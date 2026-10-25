using System.Collections.Concurrent;
using System.Diagnostics;
using Davish.Sendr;

namespace Sendr.TestFixtures;

// ActivityListener subscriptions are process-wide. The tracing test classes share this collection so that
// nothing else runs alongside them: "no listener" and "sampled out" scenarios need to be the only listener.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TracingCollection
{
    public const string Name = "Tracing";
}

/// <summary>A listener on one ActivitySource that records the spans it sees, until disposed.</summary>
public sealed class SpanCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private int _started;

    public SpanCollector(ActivitySamplingResult sampling, string sourceName = SendrActivitySource.Name)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
            ActivityStarted = _ => Interlocked.Increment(ref _started),
            ActivityStopped = activity => _stopped.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public int Started => Volatile.Read(ref _started);

    public IReadOnlyList<Activity> Stopped => [.. _stopped];

    /// <summary>The stopped spans that belong to <paramref name="caller"/>'s trace.</summary>
    public Activity[] InTraceOf(Activity caller) => [.. _stopped.Where(span => span.TraceId == caller.TraceId)];

    public void Dispose() => _listener.Dispose();

    /// <summary>Starts the caller's own Activity, outside any ActivitySource, as the parent for Sendr's spans.</summary>
    public static Activity StartCaller() => new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();
}
