namespace Davish.Sendr.Implements;

/// <summary>
/// The compiled Sequence and Parallel groups for a notification type. Built once by
/// <c>AddNotificationHandler</c> and registered as a singleton. Dispatch itself is delegated to
/// <see cref="NotificationGroupRunner"/>, shared with the generated dispatch path so the
/// exception-aggregation logic lives in exactly one place.
/// </summary>
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

        return NotificationGroupRunner.RunBothAsync(
            Bind(sequenceSteps, typed, sp),
            Bind(parallelSteps, typed, sp),
            cancellationToken);
    }

    private static IReadOnlyList<Func<CancellationToken, Task>> Bind(
        IReadOnlyList<Func<IServiceProvider, TNotification, CancellationToken, Task>> steps,
        TNotification notification,
        IServiceProvider sp)
    {
        var bound = new Func<CancellationToken, Task>[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            bound[i] = ct => step(sp, notification, ct);
        }

        return bound;
    }
}
