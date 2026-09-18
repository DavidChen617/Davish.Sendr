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

    /// <summary>
    /// Declares that <c>Davish.Sendr.Generators</c> should also discover
    /// <c>INotificationHandler&lt;T&gt;</c> implementations declared in <typeparamref name="TMarker"/>'s
    /// own assembly — for example a domain/library project referenced via <c>ProjectReference</c>,
    /// whose handlers this compilation's own generator run can otherwise never see.
    /// </summary>
    /// <remarks>
    /// This is a compile-time-only declaration read directly from source by the source generator;
    /// the call itself does nothing at runtime. Only a direct call written inline in the
    /// <c>UseGenerators(g =&gt; ...)</c> lambda is recognized — not a call reached through a
    /// loop, a condition, a stored delegate, or a helper/extension method wrapping it.
    /// </remarks>
    /// <typeparam name="TMarker">Any type declared in the assembly to include.</typeparam>
    /// <returns>The same <see cref="GeneratedNotificationRunOptions"/> so that calls can be chained.</returns>
    public GeneratedNotificationRunOptions IncludeAssemblyOf<TMarker>() => this;
}
