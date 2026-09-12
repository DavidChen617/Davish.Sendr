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
        Services.AddScoped<TPublisher>();
        Services.AddScoped<IPublisher>(sp => sp.GetRequiredService<TPublisher>());
        return this;
    }
}
