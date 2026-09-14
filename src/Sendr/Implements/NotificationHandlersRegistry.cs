using System.Collections.Concurrent;

namespace Davish.Sendr.Implements;

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
