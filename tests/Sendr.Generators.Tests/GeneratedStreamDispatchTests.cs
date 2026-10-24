using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>stream-dispatch through the sender the source generator emits for this project.</summary>
[Feature<StreamDispatchFeature>]
public class GeneratedStreamDispatchTests : StreamDispatchSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr(o => o.UseGenerators());

    [Fact]
    public async Task GivenNoStreamHandlerDiscovered_WhenEnumerated_ThenMessageSaysNotDiscoveredBySourceGenerator()
    {
        await new Scenario()
            .Given<StreamContext>("The generated sender, with no handler for OrphanStream anywhere in this project", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is enumerated", ctx => ctx.EnumerateAll(ctx.StreamSender.SendStream(new OrphanStream(), CancellationToken.None)))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
                .Contains("The message points at the source generator", "discovered by the source generator", thrown => thrown.Exception.Message)
                .Contains("The message names the stream request type", typeof(OrphanStream).ToString(), thrown => thrown.Exception.Message)
                .Contains("The message names the item type", typeof(int).ToString(), thrown => thrown.Exception.Message)
                .DoesNotContain("The message does not suggest AddStreamRequestHandler", "AddStreamRequestHandler", thrown => thrown.Exception.Message)
            .RunAsync();
    }
}
