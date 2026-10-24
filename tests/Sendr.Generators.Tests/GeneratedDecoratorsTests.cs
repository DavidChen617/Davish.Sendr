using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>decorators through the sender and publisher the source generator emits for this project.</summary>
[Feature<DecoratorsFeature>]
public class GeneratedDecoratorsTests : DecoratorsSpec
{
    protected override IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services) =>
        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>())).AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));

    [Fact]
    public async Task GivenDecoratorOnOneNotificationHandler_WhenPublish_ThenOnlyThatHandlerIsWrapped()
    {
        await new Scenario()
            .Given<PipelineContext>("PartialNote has two handlers and only one carries [DecorateWith<SingleDecorator>]", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When("PartialNote is published and awaited", ctx =>
                ctx.Scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(new PartialNote(), CancellationToken.None))
            .Then("Only the decorated handler was wrapped, and the plain one still ran", ctx => AssertOnlyDecoratedHandlerWasWrapped(ctx.Probe))
            .RunAsync();
    }
}
