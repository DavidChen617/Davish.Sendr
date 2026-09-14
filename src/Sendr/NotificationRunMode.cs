namespace Davish.Sendr;

/// <summary>
/// How the generated notification dispatcher (<c>UseGenerators()</c>, from
/// <c>Davish.Sendr.Generators</c>) runs the handlers it discovers for a notification type.
/// Unlike the Sequence/Parallel groups configured per handler via <c>AddNotificationHandler</c>,
/// this applies uniformly to every handler the generator discovers and makes no ordering
/// guarantee among them — see <see cref="NotificationGroupRunner"/>.
/// </summary>
public enum NotificationRunMode
{
    /// <summary>
    /// Await each handler one at a time. The relative order between handlers of the same
    /// notification type is not guaranteed.
    /// </summary>
    Sequence,

    /// <summary>
    /// Start every handler before awaiting any of them.
    /// </summary>
    Parallel,
}
