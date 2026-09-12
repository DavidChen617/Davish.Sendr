using System.Runtime.ExceptionServices;

namespace Davish.Sendr.Implements;

/// <summary>
/// Non-generic dispatch surface for a notification's compiled handler groups, so
/// <see cref="Publisher"/> can run them having only the notification's runtime type.
/// </summary>
internal abstract class NotificationHandlersBase
{
    public abstract Task PublishAsync(
        INotification notification, IServiceProvider sp, CancellationToken cancellationToken);
}

/// <summary>
/// The compiled Sequence and Parallel groups for a notification type. Built once by
/// <c>AddNotificationHandler</c> and registered as a singleton.
/// </summary>
/// <remarks>
/// Sequence and Parallel run concurrently with each other. Neither stops at the first failure:
/// Sequence awaits its steps one at a time, in declared order — Parallel starts every step first
/// and awaits them all afterward, so it doesn't wait for one step to finish before starting the
/// next. Order (and whether a step is already running when the next one starts) is the only
/// thing that distinguishes the two groups. Both await every step regardless of earlier failures
/// and collect every exception, the way martinothamar/Mediator's
/// <c>ForeachAwaitPublisher</c>/<c>TaskWhenAllPublisher</c> do, rather than losing all but the
/// first the way a bare <c>await Task.WhenAll(...)</c> would. Whenever more than one exception is
/// actually present — within a group, or across both groups combined — they're thrown together
/// as one <see cref="AggregateException"/> so a caller never silently loses one failure to
/// another; if there's exactly one, it's rethrown as itself (unwrapped) so callers catching a
/// specific exception type aren't forced to unwrap an <see cref="AggregateException"/> for the
/// common single-failure case.
/// Per-handler DI scoping for the Parallel group is still a placeholder, expected to be revisited
/// when Parallel is optimized further.
/// </remarks>
internal sealed class NotificationHandlers<TNotification>(
    IReadOnlyList<Func<IServiceProvider, TNotification, CancellationToken, Task>> sequenceSteps,
    IReadOnlyList<Func<IServiceProvider, TNotification, CancellationToken, Task>> parallelSteps)
    : NotificationHandlersBase
    where TNotification : INotification
{
    public override async Task PublishAsync(
        INotification notification, IServiceProvider sp, CancellationToken cancellationToken)
    {
        var typed = (TNotification)notification;

        // Start both groups before awaiting either, so they genuinely run concurrently.
        var sequenceTask = RunSequenceAsync(typed, sp, cancellationToken);
        var parallelTask = RunParallelAsync(typed, sp, cancellationToken);

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

        // Both RunSequenceAsync and RunParallelAsync already unwrap their own single-exception
        // case, but if either surfaces an AggregateException (2+ real failures within that
        // group), flatten it here rather than nesting it as one entry — so the outer exception
        // list always holds root causes.
        AddFlattened(exceptions, sequenceException);
        AddFlattened(exceptions, parallelException);

        ThrowIfAny(exceptions);
    }

    /// <summary>
    /// Runs every Sequence step one at a time, in declared order: each step only starts after
    /// the previous one's task has completed. Does not stop at the first failure — every step
    /// still runs, and every exception is collected.
    /// </summary>
    private async Task RunSequenceAsync(TNotification notification, IServiceProvider sp, CancellationToken cancellationToken)
    {
        List<Exception>? exceptions = null;

        foreach (var step in sequenceSteps)
        {
            try
            {
                await step(sp, notification, cancellationToken);
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>(1);
                exceptions.Add(ex);
            }
        }

        if (exceptions is not null)
            ThrowIfAny(exceptions);
    }

    /// <summary>
    /// Runs every Parallel step concurrently: every step is started before any of them is
    /// awaited. Every step still runs regardless of earlier failures — including a step that
    /// throws synchronously while starting, which does not stop the remaining steps from
    /// starting — and every exception is collected.
    /// </summary>
    private async Task RunParallelAsync(TNotification notification, IServiceProvider sp, CancellationToken cancellationToken)
    {
        if (parallelSteps.Count == 0)
            return;

        var tasks = new Task[parallelSteps.Count];
        List<Exception>? exceptions = null;

        for (var i = 0; i < parallelSteps.Count; i++)
        {
            try
            {
                tasks[i] = parallelSteps[i](sp, notification, cancellationToken);
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
