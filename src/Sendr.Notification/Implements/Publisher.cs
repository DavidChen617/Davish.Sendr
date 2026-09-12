namespace Davish.Sendr.Implements;

internal sealed class Publisher(IServiceProvider sp, NotificationHandlersRegistry registry) : IPublisher
{
    public Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        var handlers = (NotificationHandlersBase?)sp.GetService(registry.GetClosedType(notification.GetType()));
        return handlers is null
            ? Task.CompletedTask
            : handlers.PublishAsync(notification, sp, cancellationToken);
    }
}
