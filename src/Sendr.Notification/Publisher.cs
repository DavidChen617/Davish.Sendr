using System.Collections.Concurrent;

namespace Davish.Sendr;

/// <summary>
/// Publishes notifications to their registered handlers. Resolve an instance from the service
/// provider after calling <c>AddSendr</c>.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to every handler registered for its runtime type, running its
    /// Sequence group in registration order. If no handlers are registered, this is a no-op —
    /// safe to call for notifications collected polymorphically (for example from an outbox)
    /// where the concrete type isn't known at the call site.
    /// </summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    Task PublishAsync(INotification notification, CancellationToken cancellationToken = default);
}

internal sealed class Publisher(IServiceProvider sp, NotificationHandlersRegistry registry) : IPublisher
{
    public Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        var handlers = (NotificationHandlersBase?)sp.GetService(registry.GetClosedType(notification.GetType()));
        return handlers is null
            ? Task.CompletedTask
            : handlers.PublishAsync(notification, sp, cancellationToken);
    }
}

/// <summary>
/// Per-container cache that maps a notification type to the closed
/// <see cref="NotificationHandlers{TNotification}"/> type registered for it, so
/// <see cref="Publisher"/> doesn't repeat the <see cref="Type.MakeGenericType"/> lookup on every
/// publish. Registered as a singleton by <c>AddSendr</c>.
/// </summary>
internal sealed class NotificationHandlersRegistry
{
    private readonly ConcurrentDictionary<Type, Type> _closedTypes = new();

    public Type GetClosedType(Type notificationType) =>
        _closedTypes.GetOrAdd(notificationType,
            static t => typeof(NotificationHandlers<>).MakeGenericType(t));
}

/// <summary>
/// Non-generic dispatch surface for a notification's compiled handler groups, so
/// <see cref="Publisher"/> can run them having only the notification's runtime type.
/// </summary>
internal abstract class NotificationHandlersBase
{
    public abstract Task PublishAsync(
        INotification notification, IServiceProvider sp, CancellationToken cancellationToken = default);
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
        INotification notification, IServiceProvider sp, CancellationToken cancellationToken = default)
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
