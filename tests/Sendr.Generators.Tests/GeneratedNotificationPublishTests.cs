using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>notification-publish through the publisher the source generator emits for this project.</summary>
[Feature<NotificationPublishFeature>]
public class GeneratedNotificationPublishTests : NotificationPublishSpec
{
    protected override IServiceCollection AddPublisherUnderTest(IServiceCollection services) =>
        services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));

    private static Func<IServiceCollection, IServiceCollection> PublisherRunningAs(string runMode) => runMode == "Parallel"
        ? services => services.AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<HandlerLibraryMarker>()))
        : services => services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));

    private static Task PublishPaired(NoteContext ctx) => ctx.Publisher.PublishAsync(new PairedNote(), CancellationToken.None);

    public static TheoryData<string> RunModes => new() { "Sequence", "Parallel" };

    // ---- RunMode ----

    [Fact]
    public async Task GivenDefaultRunMode_WhenPublishAsync_ThenEveryDiscoveredHandlerRunsOneAtATime()
    {
        await new Scenario()
            .Given<NoteContext>("The generated publisher with UseGenerators() and no RunAs, and PairedNote's two handlers yielding between start and end", ctx =>
                ctx.Build(AddPublisherUnderTest))
            .When("PairedNote is published and awaited", PublishPaired)
            .Then("Each handler ran once, and one finished before the other started, in either order", ctx =>
            {
                Assert.Equal(4, ctx.Probe.Log.Count);
                var first = ctx.Probe.Log[0].Split(' ')[0];
                var second = ctx.Probe.Log[2].Split(' ')[0];
                Assert.Equal([$"{first} start", $"{first} end", $"{second} start", $"{second} end"], ctx.Probe.Log);
                Assert.Equal([nameof(PairedX), nameof(PairedY)], new[] { first, second }.Order());
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenParallelRunMode_WhenPublishAsync_ThenEachHandlerIsStartedBeforeTheOtherCompletes()
    {
        await new Scenario()
            .Given<NoteContext>("The generated publisher with RunAs(Parallel), and PairedNote's two handlers each waiting (with a timeout) for the other to start", ctx =>
            {
                ctx.Probe.Await(nameof(PairedX), nameof(PairedY));
                ctx.Probe.Await(nameof(PairedY), nameof(PairedX));
                ctx.Build(PublisherRunningAs("Parallel"));
            })
            .When("PairedNote is published and awaited", PublishPaired)
            .Then("Both completed, so each saw the other start before finishing")
                .Equal("X and Y each ended once", [1, 1], ctx => new[] { ctx.Probe.Count("PairedX end"), ctx.Probe.Count("PairedY end") })
            .RunAsync();
    }

    public static TheoryData<string, string> RunModesAndFailures => new()
    {
        { "Sequence", "throws while starting" },
        { "Sequence", "fails asynchronously" },
        { "Parallel", "throws while starting" },
        { "Parallel", "fails asynchronously" },
    };

    [Theory]
    [MemberData(nameof(RunModesAndFailures))]
    public async Task GivenOneHandlerThrows_WhenPublishAsync_ThenOtherHandlersStillRun(string runMode, string failure)
    {
        var thrown = new NoteFailure(nameof(PairedX));

        await new Scenario()
            .Given<NoteContext>($"RunMode {runMode}, and PairedNote's handler X {failure}", ctx =>
            {
                ctx.Probe.Make(nameof(PairedX), failure == "fails asynchronously" ? NoteOutcome.FailAsync : NoteOutcome.ThrowWhileStarting, thrown);
                ctx.Build(PublisherRunningAs(runMode));
            })
            .When("PairedNote is published and awaited", PublishPaired)
            .ThenThrows<NoteFailure>("The failure reaches the caller")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
                .Equal("The other handler still finished, whichever ran first", 1, t => t.State.Probe.Count("PairedY end"))
            .RunAsync();
    }

    public static TheoryData<string, string> UnresolvableStepsByRunMode => new()
    {
        { "handler", "Sequence" },
        { "handler", "Parallel" },
        { "decorator", "Sequence" },
        { "decorator", "Parallel" },
    };

    [Theory]
    [MemberData(nameof(UnresolvableStepsByRunMode))]
    public async Task GivenHandlerOrDecoratorCannotBeResolved_WhenPublishAsync_ThenFailureIsCollectedAndOtherHandlersStillRun(string step, string runMode)
    {
        var plain = step == "handler" ? nameof(UnresolvableHandlerNotePlain) : nameof(UnresolvableDecoratorNotePlain);

        await new Scenario()
            .Given<NoteContext>($"RunMode {runMode}, and a notification whose {step} depends on an unregistered service, next to a plain handler", ctx =>
                ctx.Build(PublisherRunningAs(runMode)))
            .When("The notification is published and awaited", ctx => step == "handler"
                ? ctx.Publisher.PublishAsync(new UnresolvableHandlerNote(), CancellationToken.None)
                : ctx.Publisher.PublishAsync(new UnresolvableDecoratorNote(), CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The container's resolution failure reaches the caller as itself, not inside an AggregateException")
                .Equal("The plain handler still finished", 1, t => t.State.Probe.Count($"{plain} end"))
            .RunAsync();
    }

    // ---- Exception aggregation ----

    [Theory]
    [MemberData(nameof(RunModes))]
    public async Task GivenExactlyOneHandlerFails_WhenPublishAsync_ThenOriginalExceptionIsThrownUnwrapped(string runMode)
    {
        var thrown = new NoteFailure(nameof(PairedX));

        await new Scenario()
            .Given<NoteContext>($"RunMode {runMode}, and PairedNote's handler X fails", ctx =>
            {
                ctx.Probe.Make(nameof(PairedX), NoteOutcome.FailAsync, thrown);
                ctx.Build(PublisherRunningAs(runMode));
            })
            .When("PairedNote is published and awaited", PublishPaired)
            .ThenThrows<NoteFailure>("The single failure is thrown as itself, not as an AggregateException")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(RunModes))]
    public async Task GivenSeveralHandlersFail_WhenPublishAsync_ThenAggregateExceptionContainsEveryFailure(string runMode)
    {
        var failures = new[] { new NoteFailure(nameof(PairedX)), new NoteFailure(nameof(PairedY)) };

        await new Scenario()
            .Given<NoteContext>($"RunMode {runMode}, and both of PairedNote's handlers fail", ctx =>
            {
                ctx.Probe.Make(nameof(PairedX), NoteOutcome.FailAsync, failures[0]);
                ctx.Probe.Make(nameof(PairedY), NoteOutcome.FailAsync, failures[1]);
                ctx.Build(PublisherRunningAs(runMode));
            })
            .When("PairedNote is published and awaited", PublishPaired)
            .ThenThrows<AggregateException>("Both failures reach the caller together")
                .Equal("The inner exceptions are exactly the two failures", failures.Cast<Exception>().ToHashSet(), t => t.Exception.InnerExceptions.ToHashSet(),
                    (expected, actual) => expected.SetEquals(actual))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(RunModes))]
    public async Task GivenHandlerThrowsOperationCanceledException_WhenPublishAsync_ThenPublishFailsWithOperationCanceledException(string runMode)
    {
        await new Scenario()
            .Given<NoteContext>($"RunMode {runMode}, and PairedNote's handler X throws OperationCanceledException", ctx =>
            {
                ctx.Probe.Make(nameof(PairedX), NoteOutcome.Cancel);
                ctx.Build(PublisherRunningAs(runMode));
            })
            .When("PairedNote is published and awaited", PublishPaired)
            .ThenThrows<OperationCanceledException>("The publish fails with the cancellation, not an AggregateException")
            .RunAsync();
    }

    // ---- Token, scope and lifetime ----

    [Fact]
    public async Task GivenCancellationToken_WhenPublishAsync_ThenEveryHandlerAndDecoratorReceiveSameToken()
    {
        using var caller = new CancellationTokenSource();

        await new Scenario()
            .Given<NoteContext>("DecoratedNote's two handlers, each carrying [DecorateWith] for the outer and inner token decorators", ctx =>
                ctx.Build(AddPublisherUnderTest))
            .When("DecoratedNote is published with the caller's token", ctx => ctx.Publisher.PublishAsync(new DecoratedNote(), caller.Token))
            .Then("Two handlers and four decorators each got the caller's token")
                .Equal("Six steps saw a token", 6, ctx => ctx.Probe.Tokens.Count)
                .All("Every one of them is the caller's token", ctx => ctx.Probe.Tokens, step => Assert.Equal(caller.Token, step.Token))
            .RunAsync();
    }

    [Fact]
    public async Task GivenHandlerWithScopedDependency_WhenPublishAsync_ThenDependencyComesFromPublishersScope()
    {
        await new Scenario()
            .Given<NoteContext>("BasedNote's handler depends on the scoped ScopedMarker", ctx => ctx.Build(AddPublisherUnderTest))
            .When("BasedNote is published through an IPublisher resolved from a scope", ctx =>
                ctx.Publisher.PublishAsync(new BasedNote(), CancellationToken.None))
            .Then("The handler got that scope's ScopedMarker", ctx =>
                Assert.Same(ctx.Scope.ServiceProvider.GetRequiredService<ScopedMarker>(), Assert.Single(ctx.Probe.ScopedSeen)))
            .RunAsync();
    }

    [Fact]
    public async Task GivenSamePublisher_WhenPublishAsyncTwice_ThenNewHandlerInstanceIsCreatedEachTime()
    {
        await new Scenario()
            .Given<NoteContext>("BasedNote's handler records every instance constructed", ctx => ctx.Build(AddPublisherUnderTest))
            .When("BasedNote is published twice through the same publisher", async ctx =>
            {
                await ctx.Publisher.PublishAsync(new BasedNote(), CancellationToken.None);
                await ctx.Publisher.PublishAsync(new BasedNote(), CancellationToken.None);
            })
            .Then("A handler was constructed for each publish")
                .Equal("Two handlers were constructed", 2, ctx => ctx.Probe.HandlersConstructed.Count)
            .And("The two publishes used different instances")
                .Distinct("The constructed handlers are different objects", ctx => ctx.Probe.HandlersConstructed, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }
}
