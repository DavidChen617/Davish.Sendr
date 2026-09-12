namespace Davish.Sendr.Implements;

internal sealed class Publisher(IServiceProvider sp, NotificationHandlersRegistry registry) : IPublisher
{
    public Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        var handlers = (NotificationHandlersBase?)sp.GetService(registry.GetClosedType(notification.GetType()));
        return handlers is null
            ? Task.CompletedTask
            : handlers.PublishAsync(notification, sp, cancellationToken);
    }
}
