using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr;

/// <summary>
/// Configures <c>AddSendrNotification</c>, most notably which <see cref="IPublisher"/>
/// implementation gets registered.
/// </summary>
public sealed class NotificationOptions
{
    internal NotificationOptions(IServiceCollection services) => Services = services;

    /// <summary>
    /// The <see cref="IServiceCollection"/> being configured by <c>AddSendrNotification</c>.
    /// Exposed so an extension such as the generated <c>UseGenerators()</c> can register
    /// additional services (handlers, decorators) alongside a custom publisher.
    /// </summary>
    public IServiceCollection Services { get; }

    internal bool HasCustomPublisher { get; private set; }

    /// <summary>
    /// Registers <typeparamref name="TPublisher"/> as the <see cref="IPublisher"/>
    /// implementation, replacing Davish.Sendr.Notification's default reflection-based
    /// dispatcher. This is what <c>UseGenerators()</c> (from the <c>Davish.Sendr.Generators</c>
    /// package) calls to install the generated dispatcher; call it directly to plug in any
    /// other implementation of your own.
    /// </summary>
    /// <returns>The same <see cref="NotificationOptions"/> so that calls can be chained.</returns>
    public NotificationOptions UsePublisher<TPublisher>()
        where TPublisher : class, IPublisher
    {
        HasCustomPublisher = true;

        // Constructed here rather than via a separate Services.AddScoped<TPublisher>()
        // registration also resolved via GetRequiredService: a second registration whose factory
        // also returns the same disposable instance would mean the container captures it for
        // disposal twice instead of once, since capture happens per registration, not per unique
        // instance. Unlike SendrOptions.UseSender, there's only the one public interface here
        // (IPublisher), so this achieves exactly one disposal — not just a reduced one.
        Services.AddScoped<IPublisher>(sp => ActivatorUtilities.CreateInstance<TPublisher>(sp));
        return this;
    }
}
