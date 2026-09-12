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
/// Placeholder execution policy: Sequence and Parallel run concurrently with each other;
/// Sequence awaits its steps one at a time (fail-fast), Parallel runs all its steps via
/// <c>Task.WhenAll</c>. This does not yet address exception aggregation across the two groups,
/// or per-handler DI scoping for the Parallel group — both are expected to be revisited when
/// Parallel is optimized.
/// </remarks>
internal sealed class NotificationHandlers<TNotification>(
    IReadOnlyList<Func<IServiceProvider, TNotification, CancellationToken, Task>> sequenceSteps,
    IReadOnlyList<Func<IServiceProvider, TNotification, CancellationToken, Task>> parallelSteps)
    : NotificationHandlersBase
    where TNotification : INotification
{
    public override Task PublishAsync(
        INotification notification, IServiceProvider sp, CancellationToken cancellationToken)
    {
        var typed = (TNotification)notification;

        if (parallelSteps.Count > 0)
            return Task.WhenAll(
                RunSequenceAsync(typed, sp, cancellationToken),
                Task.WhenAll(parallelSteps.Select(step => step(sp, typed, cancellationToken))));

        return RunSequenceAsync(typed, sp, cancellationToken);
    }

    private async Task RunSequenceAsync(TNotification notification, IServiceProvider sp, CancellationToken cancellationToken)
    {
        foreach (var step in sequenceSteps)
            await step(sp, notification, cancellationToken);
    }
}
