using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>tracing through the default reflection-based sender and the default publisher.</summary>
[Collection(TracingCollection.Name)]
[Feature<TracingFeature>]
public partial class TracingTests : TracingSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr().AddProbeHandlersManually();
}
