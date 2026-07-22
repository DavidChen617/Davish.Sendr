namespace Davish.Sendr;

/// <summary>
/// Bridges a non-generic <see cref="INotificationDecorator"/> back into the
/// <see cref="INotificationHandler{TNotification}"/> chain so a handler entry's decorator
/// pipeline can be composed at registration time.
/// </summary>
internal sealed class NotificationDecoratorHandlerImpl<TNotification>(
    INotificationDecorator decorator,
    INotificationHandler<TNotification> inner)
    : INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task HandleAsync(TNotification notification, CancellationToken cancellationToken = default)
        => decorator.HandleAsync(
               notification,
               () => inner.HandleAsync(notification, cancellationToken),
               cancellationToken);
}
