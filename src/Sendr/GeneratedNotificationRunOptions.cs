namespace Davish.Sendr;

/// <summary>
/// Configures the generated <c>IPublisher</c> installed by <c>UseGenerators()</c> (from the
/// <c>Davish.Sendr.Generators</c> package), most notably how handlers of the same notification
/// run relative to each other.
/// </summary>
public sealed class GeneratedNotificationRunOptions
{
    /// <summary>
    /// How handlers of the same notification run relative to each other. Defaults to
    /// <see cref="NotificationRunMode.Sequence"/>.
    /// </summary>
    public NotificationRunMode RunMode { get; private set; } = NotificationRunMode.Sequence;

    /// <summary>
    /// Sets how handlers of the same notification run relative to each other.
    /// </summary>
    /// <returns>The same <see cref="GeneratedNotificationRunOptions"/> so that calls can be chained.</returns>
    public GeneratedNotificationRunOptions RunAs(NotificationRunMode mode)
    {
        RunMode = mode;
        return this;
    }
}
