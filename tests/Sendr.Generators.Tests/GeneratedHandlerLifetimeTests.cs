using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>handler-lifetime through the sender and publisher the source generator emits for this project.</summary>
[Feature<HandlerLifetimeFeature>]
public class GeneratedHandlerLifetimeTests : HandlerLifetimeSpec
{
    protected override IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services) =>
        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>())).AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));
}
