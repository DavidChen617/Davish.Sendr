using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>request-dispatch through the default reflection-based sender.</summary>
[Feature<RequestDispatchFeature>]
public class RequestDispatchTests : RequestDispatchSpec
{
    protected override IServiceCollection AddSenderUnderTest(IServiceCollection services) =>
        services.AddSendr().AddProbeHandlersManually();

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenNoHandlerRegistered_WhenSendAsync_ThenMessageNamesTypesAndSuggestsRegistrationCall(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"The reflection sender with no handler registered for {dispatch.OrphanType.Name}", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with it and awaited", ctx => dispatch.SendOrphan(ctx.Sender, CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
                .Contains("The message names the message type", dispatch.OrphanType.ToString(), thrown => thrown.Exception.Message)
                .Contains("The message suggests the registration call", $"services.{dispatch.RegistrationMethod}<", thrown => thrown.Exception.Message)
                .True("The message names the response type when the overload has one",
                    thrown => !dispatch.HasResponse || thrown.Exception.Message.Contains(typeof(int).ToString()))
            .RunAsync();
    }
}
