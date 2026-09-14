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
