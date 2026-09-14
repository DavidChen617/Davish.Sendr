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
    Task PublishAsync(INotification notification, CancellationToken cancellationToken);
}
