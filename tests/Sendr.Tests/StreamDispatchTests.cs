using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>stream-dispatch through the default reflection-based sender.</summary>
[Feature<StreamDispatchFeature>]
public class StreamDispatchTests : StreamDispatchSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr().AddProbeHandlersManually();

    [Fact]
    public async Task GivenNoStreamHandlerRegistered_WhenEnumerated_ThenMessageNamesTypesAndSuggestsAddStreamRequestHandler()
    {
        await new Scenario()
            .Given<StreamContext>("The reflection sender with no handler registered for OrphanStream", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is enumerated", ctx => ctx.EnumerateAll(ctx.StreamSender.SendStream(new OrphanStream(), CancellationToken.None)))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
                .Contains("The message names the stream request type", typeof(OrphanStream).ToString(), thrown => thrown.Exception.Message)
                .Contains("The message names the item type", typeof(int).ToString(), thrown => thrown.Exception.Message)
                .Contains("The message suggests the registration call", "services.AddStreamRequestHandler<", thrown => thrown.Exception.Message)
            .RunAsync();
    }
}
