namespace Davish.Sendr;

/// <summary>
/// Invokes the next step in a notification handler's pipeline (the inner decorator or the
/// handler itself).
/// </summary>
/// <returns>A task that completes when the next step has run.</returns>
public delegate Task NotificationHandlerDelegate();

/// <summary>
/// Adds cross-cutting behaviour (such as logging or retries) around a single notification
/// handler entry. A single decorator instance can be applied to any notification type; the
/// notification type is a parameter of <see cref="HandleAsync"/> rather than of the interface.
/// </summary>
public interface INotificationDecorator
{
    /// <summary>
    /// Wraps the handling of <paramref name="notification"/>, calling <paramref name="next"/> to
    /// run the inner pipeline step.
    /// </summary>
    /// <typeparam name="TNotification">The type of notification being handled.</typeparam>
    /// <param name="notification">The notification being handled.</param>
    /// <param name="next">Invokes the inner decorator or the handler entry.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the notification has been handled.</returns>
    Task HandleAsync<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
        where TNotification : INotification;
}
