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
        // Snapshotted up front: steps is typed as IReadOnlyList, but the caller may have handed
        // in a mutable List<T> they still hold a reference to. If a step's own body appends to
        // that same list while this loop is mid-enumeration (observed with a step that does so
        // synchronously), a plain foreach over the live list throws InvalidOperationException
        // ("Collection was modified"). Copying once before iterating decouples this run from any
        // later mutation of the caller's list.
        var snapshot = steps as Func<CancellationToken, Task>[] ?? steps.ToArray();
        List<Exception>? exceptions = null;

        foreach (var step in snapshot)
        {
            try
            {
                await step(cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions ??= [with(1)];
                exceptions.Add(ex);
            }
        }

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
        // See RunSequenceAsync: snapshotted up front so a step that appends to the caller's own
        // mutable list while this loop is running can't grow steps.Count mid-loop out from under
        // the pre-sized tasks array (observed as IndexOutOfRangeException without this).
        var snapshot = steps as Func<CancellationToken, Task>[] ?? steps.ToArray();

        if (snapshot.Length == 0)
            return;

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
                exceptions.Add(ex);
            }
        }

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
        var sequenceTask = RunSequenceAsync(sequenceSteps, cancellationToken);
        var parallelTask = RunParallelAsync(parallelSteps, cancellationToken);

        Exception? sequenceException = null;
        Exception? parallelException = null;

        try
        {
            await sequenceTask;
        }
        catch (Exception ex)
        {
            sequenceException = ex;
        }

        try
        {
            await parallelTask;
        }
        catch (Exception ex)
        {
            parallelException = ex;
        }

        var exceptions = new List<Exception>();
        AddFlattened(exceptions, sequenceException);
        AddFlattened(exceptions, parallelException);

        ThrowIfAny(exceptions);
    }

    private static void AddFlattened(List<Exception> exceptions, Exception? exception)
    {
        switch (exception)
        {
            case null:
                return;
            case AggregateException aggregate:
                exceptions.AddRange(aggregate.InnerExceptions);
                return;
            default:
                exceptions.Add(exception);
                return;
        }
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
