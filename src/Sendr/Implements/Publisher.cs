namespace Davish.Sendr.Implements;

internal sealed class Publisher(IServiceProvider sp, NotificationHandlersRegistry registry) : IPublisher
{
    public Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        if (notification is null)
            throw new ArgumentNullException(nameof(notification));

        var handlers = (NotificationHandlersBase?)sp.GetService(registry.GetClosedType(notification.GetType()));
        if (handlers is null)
            return Task.CompletedTask;

        if (!SendrActivitySource.IsEnabled)
            return handlers.PublishAsync(notification, sp, cancellationToken);

        return SendrActivitySource.Invoke(SendrActivitySource.NotificationKind, notification.GetType(),
            (handlers, sp, notification, cancellationToken),
            static s => s.handlers.PublishAsync(s.notification, s.sp, s.cancellationToken));
    }
}
