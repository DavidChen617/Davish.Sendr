using System.Runtime.ExceptionServices;

namespace Davish.Sendr;

/// <summary>
/// Shared dispatch primitives for running a notification's handler steps: collects every
/// exception instead of losing all but the first (the way a bare <c>await Task.WhenAll(...)</c>
/// would), and rethrows a lone exception unwrapped so callers catching a specific exception type
/// aren't forced to unwrap an <see cref="AggregateException"/> for the common single-failure
/// case. Used by both the manual <c>AddNotificationHandler</c> registration path and by
/// <c>Davish.Sendr.Generators</c>-emitted code, so this logic exists in exactly one place.
/// </summary>
public static class NotificationGroupRunner
{
    /// <summary>
    /// Runs every step one at a time, in order: each step only starts after the previous one's
    /// task has completed. Does not stop at the first failure — every step still runs, and every
    /// exception is collected.
    /// </summary>
    public static async Task RunSequenceAsync(
        IReadOnlyList<Func<CancellationToken, Task>> steps, CancellationToken cancellationToken)
    {
        var exceptions = await CollectSequenceExceptionsAsync(steps, cancellationToken);
        if (exceptions is not null)
            ThrowIfAny(exceptions);
    }

    /// <summary>
    /// Runs every step concurrently: every step is started before any of them is awaited. Every
    /// step still runs regardless of earlier failures — including a step that throws
    /// synchronously while starting, which does not stop the remaining steps from starting —
    /// and every exception is collected.
    /// </summary>
    public static async Task RunParallelAsync(
        IReadOnlyList<Func<CancellationToken, Task>> steps, CancellationToken cancellationToken)
    {
        var exceptions = await CollectParallelExceptionsAsync(steps, cancellationToken);
        if (exceptions is not null)
            ThrowIfAny(exceptions);
    }

    /// <summary>
    /// Runs a Sequence group and a Parallel group concurrently with each other (rather than one
    /// after the other), combining exceptions from both into one result: zero stay silent,
    /// exactly one is rethrown as itself, and two or more are combined into one
    /// <see cref="AggregateException"/>.
    /// </summary>
    public static async Task RunBothAsync(
        IReadOnlyList<Func<CancellationToken, Task>> sequenceSteps,
        IReadOnlyList<Func<CancellationToken, Task>> parallelSteps,
        CancellationToken cancellationToken)
    {
        // Snapshotted together, synchronously, before either group starts running. Calling an
        // async method runs its body synchronously up to its first genuine suspension point —
        // if Sequence's only step completes synchronously (a real possibility: awaiting an
        // already-completed Task doesn't suspend either), CollectSequenceExceptionsAsync can run
        // to completion before this method's next line ever executes. Without snapshotting here
        // first, CollectParallelExceptionsAsync would take its own snapshot only once it actually
        // starts — which could be after whatever Sequence's synchronous portion already did,
        // including mutating the very list Parallel is about to iterate.
        var sequenceSnapshot = sequenceSteps.ToArray();
        var parallelSnapshot = parallelSteps.ToArray();

        var sequenceTask = CollectSequenceExceptionsAsync(sequenceSnapshot, cancellationToken);
        var parallelTask = CollectParallelExceptionsAsync(parallelSnapshot, cancellationToken);

        // Collected as raw lists, never thrown-then-caught-then-reassembled in between: combining
        // Sequence's and Parallel's failures used to mean throwing each group's own
        // AggregateException and unwrapping it back to individual exceptions in the caller,
        // assuming any AggregateException seen there was one this class had just built itself. A
        // handler that faults with its own AggregateException (e.g. via Task.FromException) is
        // indistinguishable from that assumption's point of view, so it got torn apart too,
        // losing its identity/message/stack trace — and if that handler-owned AggregateException
        // happened to have zero inner exceptions (a legal, meaningful exception on its own),
        // flattening it produced literally nothing, turning a real failure into a silent success.
        // Keeping each raw exception as its own list entry from the moment it's caught, all the
        // way to the one ThrowIfAny call at the end, avoids ever needing to guess whose
        // AggregateException is whose.
        var sequenceExceptions = await sequenceTask;
        var parallelExceptions = await parallelTask;

        List<Exception>? exceptions = null;

        if (sequenceExceptions is not null)
        {
            exceptions ??= [with(sequenceExceptions.Count + (parallelExceptions?.Count ?? 0))];
            exceptions.AddRange(sequenceExceptions);
        }

        if (parallelExceptions is not null)
        {
            exceptions ??= [with(parallelExceptions.Count)];
            exceptions.AddRange(parallelExceptions);
        }

        if (exceptions is not null)
            ThrowIfAny(exceptions);
    }

    private static async Task<List<Exception>?> CollectSequenceExceptionsAsync(
        IReadOnlyList<Func<CancellationToken, Task>> steps, CancellationToken cancellationToken)
    {
        // Always copied, even when steps is already an array: a step can mutate the caller's own
        // backing collection as a side effect of running. A List's structural mutation corrupts
        // this loop outright (InvalidOperationException); an array passed through unmodified would
        // still let a step replace one of ITS OWN later elements out from under an iteration that
        // hasn't reached it yet. Copying unconditionally closes both gaps the same way, regardless
        // of which shape the caller passed in.
        var snapshot = steps.ToArray();
        List<Exception>? exceptions = null;

        foreach (var step in snapshot)
        {
            Task task;
            try
            {
                task = step(cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions ??= [with(1)];
                exceptions.Add(ex);
                continue;
            }

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                exceptions ??= [with(1)];
                AddFault(exceptions, task, ex);
            }
        }

        return exceptions;
    }

    private static async Task<List<Exception>?> CollectParallelExceptionsAsync(
        IReadOnlyList<Func<CancellationToken, Task>> steps, CancellationToken cancellationToken)
    {
        // See CollectSequenceExceptionsAsync for why this is copied unconditionally.
        var snapshot = steps.ToArray();

        if (snapshot.Length == 0)
            return null;

        var tasks = new Task[snapshot.Length];
        List<Exception>? exceptions = null;

        for (var i = 0; i < snapshot.Length; i++)
        {
            try
            {
                tasks[i] = snapshot[i](cancellationToken);
            }
            catch (Exception ex)
            {
                tasks[i] = Task.CompletedTask;
                exceptions ??= [with(1)];
                exceptions.Add(ex);
            }
        }

        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                exceptions ??= [with(1)];
                AddFault(exceptions, task, ex);
            }
        }

        return exceptions;
    }

    /// <summary>
    /// Records every exception a step's task actually faulted with. <c>await</c> only ever
    /// surfaces the first exception of a faulted <see cref="Task"/> (via <paramref name="caught"/>),
    /// silently discarding the rest — a step whose task was completed via
    /// <c>TaskCompletionSource.SetException</c> with more than one exception would otherwise lose
    /// all but the first. <see cref="Task.Exception"/> preserves the complete set, so it's used
    /// instead whenever the task actually faulted (as opposed to being canceled, which carries no
    /// <see cref="Task.Exception"/> and is recorded as the single exception <c>await</c> caught).
    /// </summary>
    private static void AddFault(List<Exception> exceptions, Task task, Exception caught)
    {
        if (task.IsFaulted && task.Exception is { } aggregate)
            exceptions.AddRange(aggregate.InnerExceptions);
        else
            exceptions.Add(caught);
    }

    /// <summary>
    /// Throws nothing for zero exceptions, rethrows the single exception as itself (preserving
    /// its original stack trace) for exactly one, or combines them into one
    /// <see cref="AggregateException"/> for more than one.
    /// </summary>
    private static void ThrowIfAny(IReadOnlyList<Exception> exceptions)
    {
        switch (exceptions.Count)
        {
            case 0:
                return;
            case 1:
                ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
                return;
            default:
                throw new AggregateException(exceptions);
        }
    }
}
