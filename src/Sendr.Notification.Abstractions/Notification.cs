namespace Davish.Sendr;

/// <summary>
/// Marker interface for a notification that can be published to zero or more handlers through
/// an <c>IPublisher</c>. Unlike <c>IRequest</c>, a notification is not required to
/// have exactly one handler.
/// </summary>
public interface INotification;
