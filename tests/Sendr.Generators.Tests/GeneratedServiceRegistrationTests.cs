using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;

namespace Sendr.Generators.Tests;

[Feature<ServiceRegistrationFeature>]
public class GeneratedServiceRegistrationTests
{
    [Fact]
    public void GivenAddSendrWithUseGenerators_WhenResolvingISender_ThenSenderIsReturned()
    {
        new Scenario()
            .Given("AddSendr(o => o.UseGenerators())", () =>
                new ServiceCollection().AddSendr(o => o.UseGenerators()).BuildServiceProvider())
            .When("ISender is resolved from a scope", provider =>
                provider.CreateScope().ServiceProvider.GetRequiredService<ISender>())
            .Then("A sender is returned")
                .NotNull("The resolved ISender is not null", sender => sender)
            .Run();
    }

    [Fact]
    public void GivenAddSendrWithUseGenerators_WhenResolvingISenderAcrossScopes_ThenInstanceIsScoped()
    {
        new Scenario()
            .Given<ScopedResolution<ISender>>("AddSendr(o => o.UseGenerators())", ctx =>
                ctx.Provider = new ServiceCollection().AddSendr(o => o.UseGenerators()).BuildServiceProvider())
            .When("ISender is resolved twice in one scope and once in another", ctx => ctx.ResolveAcrossScopes())
            .Then("The instance is shared within a scope")
                .True("Both resolutions in the first scope are the same object", ctx => ReferenceEquals(ctx.First, ctx.SecondInSameScope))
            .And("The instance is not shared across scopes")
                .False("The other scope gets a different object", ctx => ReferenceEquals(ctx.First, ctx.InOtherScope))
            .Run();
    }

    [Fact]
    public void GivenAddSendrNotificationWithUseGenerators_WhenResolvingIPublisher_ThenPublisherIsReturned()
    {
        new Scenario()
            .Given("AddSendrNotification(o => o.UseGenerators())", () =>
                new ServiceCollection().AddSendrNotification(o => o.UseGenerators()).BuildServiceProvider())
            .When("IPublisher is resolved from a scope", provider =>
                provider.CreateScope().ServiceProvider.GetRequiredService<IPublisher>())
            .Then("A publisher is returned")
                .NotNull("The resolved IPublisher is not null", publisher => publisher)
            .Run();
    }

    [Fact]
    public void GivenAddSendrNotificationWithUseGenerators_WhenResolvingIPublisherAcrossScopes_ThenInstanceIsScoped()
    {
        new Scenario()
            .Given<ScopedResolution<IPublisher>>("AddSendrNotification(o => o.UseGenerators())", ctx =>
                ctx.Provider = new ServiceCollection().AddSendrNotification(o => o.UseGenerators()).BuildServiceProvider())
            .When("IPublisher is resolved twice in one scope and once in another", ctx => ctx.ResolveAcrossScopes())
            .Then("The instance is shared within a scope")
                .True("Both resolutions in the first scope are the same object", ctx => ReferenceEquals(ctx.First, ctx.SecondInSameScope))
            .And("The instance is not shared across scopes")
                .False("The other scope gets a different object", ctx => ReferenceEquals(ctx.First, ctx.InOtherScope))
            .Run();
    }

    private sealed class ScopedResolution<TService> where TService : notnull
    {
        public ServiceProvider Provider { get; set; } = null!;
        public TService? First { get; private set; }
        public TService? SecondInSameScope { get; private set; }
        public TService? InOtherScope { get; private set; }

        public void ResolveAcrossScopes()
        {
            using (var scope = Provider.CreateScope())
            {
                First = scope.ServiceProvider.GetRequiredService<TService>();
                SecondInSameScope = scope.ServiceProvider.GetRequiredService<TService>();
            }

            using (var other = Provider.CreateScope())
                InOtherScope = other.ServiceProvider.GetRequiredService<TService>();
        }
    }
}
