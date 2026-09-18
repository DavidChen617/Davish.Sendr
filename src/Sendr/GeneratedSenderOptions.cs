namespace Davish.Sendr;

/// <summary>
/// Configures the generated <c>ISender</c>/<c>IStreamSender</c> installed by <c>UseGenerators()</c>
/// (from the <c>Davish.Sendr.Generators</c> package), most notably which additional assemblies'
/// handlers are discovered at compile time alongside this compilation's own.
/// </summary>
public sealed class GeneratedSenderOptions
{
    /// <summary>
    /// Declares that <c>Davish.Sendr.Generators</c> should also discover
    /// <c>IRequestHandler</c>/<c>IStreamRequestHandler</c>/<c>ICommandHandler</c>/<c>IQueryHandler</c>
    /// implementations declared in <typeparamref name="TMarker"/>'s own assembly — for example a
    /// domain/library project referenced via <c>ProjectReference</c>, whose handlers this
    /// compilation's own generator run can otherwise never see.
    /// </summary>
    /// <remarks>
    /// This is a compile-time-only declaration read directly from source by the source generator;
    /// the call itself does nothing at runtime. Only a direct call written inline in the
    /// <c>UseGenerators(g =&gt; ...)</c> lambda is recognized — not a call reached through a
    /// loop, a condition, a stored delegate, or a helper/extension method wrapping it.
    /// </remarks>
    /// <typeparam name="TMarker">Any type declared in the assembly to include.</typeparam>
    /// <returns>The same <see cref="GeneratedSenderOptions"/> so that calls can be chained.</returns>
    public GeneratedSenderOptions IncludeAssemblyOf<TMarker>() => this;
}
