using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr;

/// <summary>
/// Configures <c>AddSendr</c>, most notably which <see cref="ISender"/>/<see cref="IStreamSender"/>
/// implementation gets registered.
/// </summary>
public sealed class SendrOptions
{
    internal SendrOptions(IServiceCollection services) => Services = services;

    /// <summary>
    /// The <see cref="IServiceCollection"/> being configured by <c>AddSendr</c>. Exposed so an
    /// extension such as the generated <c>UseGenerators()</c> can register additional services
    /// (handlers, decorators) alongside a custom sender.
    /// </summary>
    public IServiceCollection Services { get; }

    internal bool HasCustomSender { get; private set; }

    /// <summary>
    /// Registers <typeparamref name="TSender"/> as the <see cref="ISender"/>/<see cref="IStreamSender"/>
    /// implementation, replacing Davish.Sendr's default reflection-based dispatcher. This is what
    /// <c>UseGenerators()</c> (from the <c>Davish.Sendr.Generators</c> package) calls to install
    /// the generated, reflection-free dispatcher; call it directly to plug in any other
    /// implementation of your own.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TSender"/> is resolved as both <see cref="ISender"/> and
    /// <see cref="IStreamSender"/> — the same instance within a given scope, never a separately
    /// registered <typeparamref name="TSender"/> — but each of those two registrations is still
    /// its own distinct DI registration, so if <typeparamref name="TSender"/> implements
    /// <see cref="IDisposable"/>/<see cref="IAsyncDisposable"/> and a scope resolves both
    /// <see cref="ISender"/> and <see cref="IStreamSender"/>, the container will call its
    /// disposal method for each of those two registrations. Implement disposal idempotently
    /// (safe to invoke more than once) if <typeparamref name="TSender"/> is disposable.
    /// </remarks>
    /// <returns>The same <see cref="SendrOptions"/> so that calls can be chained.</returns>
    public SendrOptions UseSender<TSender>()
        where TSender : class, ISender, IStreamSender
    {
        HasCustomSender = true;

        // ISender is the only registration whose factory actually constructs TSender —
        // IStreamSender resolves through it instead of also constructing/resolving TSender on its
        // own. A third, separate TSender registration alongside ISender/IStreamSender would mean
        // the container captures the same disposable instance for disposal up to three times over
        // (once per registration whose factory returns it); going through ISender's own
        // resolution keeps that at two. See the remarks above for the residual.
        Services.AddScoped<ISender>(sp => ActivatorUtilities.CreateInstance<TSender>(sp));
        Services.AddScoped<IStreamSender>(sp => sp.GetRequiredService<ISender>());
        return this;
    }
}
