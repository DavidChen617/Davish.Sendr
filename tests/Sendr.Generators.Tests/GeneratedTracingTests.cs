using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>tracing through the sender and publisher the source generator emits for this project.</summary>
[Collection(TracingCollection.Name)]
[Feature<TracingFeature>]
public partial class GeneratedTracingTests : TracingSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));
}
