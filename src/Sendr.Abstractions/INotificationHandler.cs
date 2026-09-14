namespace Davish.Sendr;

/// <summary>
/// Handles a notification published through an <c>IPublisher</c>. A notification type can
/// have any number of handlers, including zero.
/// </summary>
/// <typeparam name="TNotification">The type of notification to handle.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the specified <paramref name="notification"/>.
    /// </summary>
    /// <param name="notification">The notification to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the notification has been handled.</returns>
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken);
}
