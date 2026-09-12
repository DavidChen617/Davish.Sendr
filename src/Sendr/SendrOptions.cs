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
    /// <returns>The same <see cref="SendrOptions"/> so that calls can be chained.</returns>
    public SendrOptions UseSender<TSender>()
        where TSender : class, ISender, IStreamSender
    {
        HasCustomSender = true;
        Services.AddScoped<TSender>();
        Services.AddScoped<ISender>(sp => sp.GetRequiredService<TSender>());
        Services.AddScoped<IStreamSender>(sp => sp.GetRequiredService<TSender>());
        return this;
    }
}
