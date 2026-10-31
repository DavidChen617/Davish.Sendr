using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>request-dispatch through the sender the source generator emits for this project.</summary>
[Feature<RequestDispatchFeature>]
public class GeneratedRequestDispatchTests : RequestDispatchSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenNoHandlerDiscovered_WhenSendAsync_ThenMessageSaysNotDiscoveredBySourceGenerator(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"The generated sender, with no handler for {dispatch.OrphanType.Name} anywhere in this project", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with it and awaited", ctx => dispatch.SendOrphan(ctx.Sender, CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
                .Contains("The message points at the source generator", "discovered by the source generator", thrown => thrown.Exception.Message)
                .Contains("The message names the message type", dispatch.OrphanType.ToString(), thrown => thrown.Exception.Message)
                .Contains("The message says manual registration means dropping UseGenerators()", "stop calling UseGenerators()", thrown => thrown.Exception.Message)
                .DoesNotContain("The message no longer suggests registering manually alongside UseGenerators()", "instead of relying on UseGenerators()", thrown => thrown.Exception.Message)
                .DoesNotContain("The message does not suggest AddRequestHandler", "AddRequestHandler", thrown => thrown.Exception.Message)
                .DoesNotContain("The message does not suggest AddCommandHandler", "AddCommandHandler", thrown => thrown.Exception.Message)
                .DoesNotContain("The message does not suggest AddQueryHandler", "AddQueryHandler", thrown => thrown.Exception.Message)
            .RunAsync();
    }
}
