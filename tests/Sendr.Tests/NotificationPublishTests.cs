using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>notification-publish through the default publisher and its Sequence / Parallel groups.</summary>
[Feature<NotificationPublishFeature>]
public class NotificationPublishTests : NotificationPublishSpec
{
    protected override IServiceCollection AddPublisherUnderTest(IServiceCollection services) => services
        .AddSendrNotification()
        .AddNotificationHandler<BasedNote>(x => x.Handler.Sequence.With<BasedNoteHandler>())
        .AddNotificationHandler<ProbeNote>(x => x.Handler.Sequence.With<NoteA>());

    private static Func<IServiceCollection, IServiceCollection> ProbeNoteWith(Action<NotificationHandlerOptions<ProbeNote>> configure) =>
        services => services.AddSendrNotification().AddNotificationHandler(configure);

    private static Task Publish(NoteContext ctx) => ctx.Publisher.PublishAsync(new ProbeNote(), CancellationToken.None);

    // ---- Groups ----

    [Fact]
    public async Task GivenSequenceGroupWithSeveralHandlers_WhenPublishAsync_ThenHandlersRunOneAfterAnotherInRegistrationOrder()
    {
        await new Scenario()
            .Given<NoteContext>("A Sequence group of A, B and C, each yielding between its start and end", ctx =>
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>().With<NoteB>().With<NoteC>())))
            .When("ProbeNote is published and awaited", Publish)
            .Then("Each handler finished before the next one started, in registration order")
                .Equal("A start, A end, B start, B end, C start, C end",
                    ["NoteA start", "NoteA end", "NoteB start", "NoteB end", "NoteC start", "NoteC end"], ctx => ctx.Probe.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenParallelGroupWithSeveralHandlers_WhenPublishAsync_ThenEveryHandlerRuns()
    {
        await new Scenario()
            .Given<NoteContext>("A Parallel group of A, B and C", ctx =>
                ctx.Build(ProbeNoteWith(x => x.Handler.Parallel.With<NoteA>().With<NoteB>().With<NoteC>())))
            .When("ProbeNote is published and awaited", Publish)
            .Then("Every handler ran exactly once")
                .Equal("A, B and C each ended once", [1, 1, 1], ctx => new[] { ctx.Probe.Count("NoteA end"), ctx.Probe.Count("NoteB end"), ctx.Probe.Count("NoteC end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenParallelGroupWithTwoHandlers_WhenPublishAsync_ThenEachIsStartedBeforeTheOtherCompletes()
    {
        await new Scenario()
            .Given<NoteContext>("A Parallel group of A and B, each waiting (with a timeout) for the other to start", ctx =>
            {
                ctx.Probe.Await(nameof(NoteA), nameof(NoteB));
                ctx.Probe.Await(nameof(NoteB), nameof(NoteA));
                ctx.Build(ProbeNoteWith(x => x.Handler.Parallel.With<NoteA>().With<NoteB>()));
            })
            .When("ProbeNote is published and awaited", Publish)
            .Then("Both completed, so each saw the other start before finishing")
                .Equal("A and B each ended once", [1, 1], ctx => new[] { ctx.Probe.Count("NoteA end"), ctx.Probe.Count("NoteB end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenSequenceAndParallelGroups_WhenPublishAsync_ThenGroupsRunConcurrentlyAndBothComplete()
    {
        await new Scenario()
            .Given<NoteContext>("Sequence [A, B] and Parallel [C, D], where A waits (with a timeout) for C to start", ctx =>
            {
                ctx.Probe.Await(nameof(NoteA), nameof(NoteC));
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>().With<NoteD>();
                }));
            })
            .When("ProbeNote is published and awaited", Publish)
            .Then("A saw C start while still running, and every handler in both groups finished")
                .Equal("A, B, C and D each ended once", [1, 1, 1, 1],
                    ctx => new[] { ctx.Probe.Count("NoteA end"), ctx.Probe.Count("NoteB end"), ctx.Probe.Count("NoteC end"), ctx.Probe.Count("NoteD end") })
            .RunAsync();
    }

    public static TheoryData<string> Groups => new() { "Sequence", "Parallel" };

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenSameHandlerAddedTwiceToGroup_WhenPublishAsync_ThenItRunsTwice(string group)
    {
        await new Scenario()
            .Given<NoteContext>($"The {group} group adds A twice, once with the outer decorator and once with the inner one", ctx =>
                ctx.Build(ProbeNoteWith(x =>
                {
                    if (group == "Sequence")
                        x.Handler.Sequence
                            .With<NoteA>(h => h.Decorator.With<NoteOuterTokenDecorator>())
                            .With<NoteA>(h => h.Decorator.With<NoteInnerTokenDecorator>());
                    else
                        x.Handler.Parallel
                            .With<NoteA>(h => h.Decorator.With<NoteOuterTokenDecorator>())
                            .With<NoteA>(h => h.Decorator.With<NoteInnerTokenDecorator>());
                })))
            .When("ProbeNote is published and awaited", Publish)
            .Then("A ran twice")
                .Equal("A ended twice", 2, ctx => ctx.Probe.Count("NoteA end"))
            .And("Each run went through its own entry's decorator")
                .Equal("The outer and the inner decorator each ran once",
                    ["inner decorator", "outer decorator"],
                    ctx => ctx.Probe.Tokens.Select(t => t.Step).Where(step => step.EndsWith("decorator")).Order())
            .RunAsync();
    }

    // ---- Failures do not stop other handlers ----

    public static TheoryData<string> SequenceFailures => new() { "first throws while starting", "middle throws while starting", "middle fails asynchronously" };

    [Theory]
    [MemberData(nameof(SequenceFailures))]
    public async Task GivenSequenceHandlerThrows_WhenPublishAsync_ThenLaterHandlersStillRunBeforePublishFails(string failure)
    {
        var (failing, outcome, expectedLog) = failure switch
        {
            "first throws while starting" => (nameof(NoteA), NoteOutcome.ThrowWhileStarting,
                new[] { "NoteA start", "NoteB start", "NoteB end", "NoteC start", "NoteC end" }),
            "middle throws while starting" => (nameof(NoteB), NoteOutcome.ThrowWhileStarting,
                new[] { "NoteA start", "NoteA end", "NoteB start", "NoteC start", "NoteC end" }),
            _ => (nameof(NoteB), NoteOutcome.FailAsync,
                new[] { "NoteA start", "NoteA end", "NoteB start", "NoteC start", "NoteC end" }),
        };
        var thrown = new NoteFailure(failing);

        await new Scenario()
            .Given<NoteContext>($"A Sequence group of A, B and C, where the {failure}", ctx =>
            {
                ctx.Probe.Make(failing, outcome, thrown);
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>().With<NoteB>().With<NoteC>()));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<NoteFailure>("The failure reaches the caller")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
                .Equal("The handlers after it still ran, in registration order", expectedLog, t => t.State.Probe.Log)
            .RunAsync();
    }

    public static TheoryData<string> ParallelFailures => new() { "first added throws while starting", "a handler fails asynchronously" };

    [Theory]
    [MemberData(nameof(ParallelFailures))]
    public async Task GivenParallelHandlerThrows_WhenPublishAsync_ThenOtherHandlersStillStartAndComplete(string failure)
    {
        var thrown = new NoteFailure(nameof(NoteA));

        await new Scenario()
            .Given<NoteContext>($"A Parallel group of A, B and C, where A {(failure == "a handler fails asynchronously" ? "fails asynchronously" : "throws while starting")}", ctx =>
            {
                ctx.Probe.Make(nameof(NoteA), failure == "a handler fails asynchronously" ? NoteOutcome.FailAsync : NoteOutcome.ThrowWhileStarting, thrown);
                ctx.Build(ProbeNoteWith(x => x.Handler.Parallel.With<NoteA>().With<NoteB>().With<NoteC>()));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<NoteFailure>("The failure reaches the caller")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
                .Equal("B and C still started and finished", [1, 1], t => new[] { t.State.Probe.Count("NoteB end"), t.State.Probe.Count("NoteC end") })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenOneGroupFails_WhenPublishAsync_ThenOtherGroupStillRunsToCompletion(string failingGroup)
    {
        var failing = failingGroup == "Sequence" ? nameof(NoteA) : nameof(NoteC);
        var thrown = new NoteFailure(failing);

        await new Scenario()
            .Given<NoteContext>($"Sequence [A, B] and Parallel [C, D], where the first handler of the {failingGroup} group fails", ctx =>
            {
                ctx.Probe.Make(failing, NoteOutcome.FailAsync, thrown);
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>().With<NoteD>();
                }));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<NoteFailure>("The failure reaches the caller")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
                .Equal("Every other handler, in both groups, finished",
                    failingGroup == "Sequence" ? ["NoteB end", "NoteC end", "NoteD end"] : ["NoteA end", "NoteB end", "NoteD end"],
                    t => t.State.Probe.Log.Where(entry => entry.EndsWith(" end")).Order())
            .RunAsync();
    }

    public static TheoryData<string, string> UnresolvableSteps => new()
    {
        { "handler", "Sequence" },
        { "handler", "Parallel" },
        { "decorator", "Sequence" },
        { "decorator", "Parallel" },
    };

    [Theory]
    [MemberData(nameof(UnresolvableSteps))]
    public async Task GivenHandlerOrDecoratorCannotBeResolved_WhenPublishAsync_ThenFailureIsCollectedAndOtherHandlersStillRun(string step, string group)
    {
        var plain = step == "handler" ? nameof(UnresolvableHandlerNotePlain) : nameof(UnresolvableDecoratorNotePlain);

        await new Scenario()
            .Given<NoteContext>($"A {group} entry whose {step} depends on an unregistered service, next to a plain handler", ctx =>
                ctx.Build(services => step == "handler"
                    ? services.AddSendrNotification().AddNotificationHandler<UnresolvableHandlerNote>(x =>
                    {
                        if (group == "Sequence")
                            x.Handler.Sequence.With<MissingDependencyHandler>().With<UnresolvableHandlerNotePlain>();
                        else
                            x.Handler.Parallel.With<MissingDependencyHandler>().With<UnresolvableHandlerNotePlain>();
                    })
                    : services.AddSendrNotification().AddNotificationHandler<UnresolvableDecoratorNote>(x =>
                    {
                        if (group == "Sequence")
                            x.Handler.Sequence.With<DecoratorBlockedHandler>(h => h.Decorator.With<MissingDependencyDecorator>()).With<UnresolvableDecoratorNotePlain>();
                        else
                            x.Handler.Parallel.With<DecoratorBlockedHandler>(h => h.Decorator.With<MissingDependencyDecorator>()).With<UnresolvableDecoratorNotePlain>();
                    })))
            .When("The notification is published and awaited", ctx => step == "handler"
                ? ctx.Publisher.PublishAsync(new UnresolvableHandlerNote(), CancellationToken.None)
                : ctx.Publisher.PublishAsync(new UnresolvableDecoratorNote(), CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The container's resolution failure reaches the caller as itself, not inside an AggregateException")
                .Equal("The plain handler still finished", 1, t => t.State.Probe.Count($"{plain} end"))
            .RunAsync();
    }

    // ---- Exception aggregation ----

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenExactlyOneHandlerFails_WhenPublishAsync_ThenOriginalExceptionIsThrownUnwrapped(string failingGroup)
    {
        var failing = failingGroup == "Sequence" ? nameof(NoteA) : nameof(NoteC);
        var thrown = new NoteFailure(failing);

        await new Scenario()
            .Given<NoteContext>($"Sequence [A, B] and Parallel [C, D], where one handler in the {failingGroup} group fails", ctx =>
            {
                ctx.Probe.Make(failing, NoteOutcome.FailAsync, thrown);
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>().With<NoteD>();
                }));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<NoteFailure>("The single failure is thrown as itself, not as an AggregateException")
                .True("It is the handler's own exception", t => ReferenceEquals(t.Exception, thrown))
            .RunAsync();
    }

    public static TheoryData<string> FailurePlacements => new() { "both in Sequence", "both in Parallel", "one in each group" };

    [Theory]
    [MemberData(nameof(FailurePlacements))]
    public async Task GivenSeveralHandlersFail_WhenPublishAsync_ThenAggregateExceptionContainsEveryFailure(string placement)
    {
        var (first, second) = placement switch
        {
            "both in Sequence" => (nameof(NoteA), nameof(NoteB)),
            "both in Parallel" => (nameof(NoteC), nameof(NoteD)),
            _ => (nameof(NoteA), nameof(NoteC)),
        };
        var failures = new[] { new NoteFailure(first), new NoteFailure(second) };

        await new Scenario()
            .Given<NoteContext>($"Sequence [A, B] and Parallel [C, D], where two handlers fail, {placement}", ctx =>
            {
                ctx.Probe.Make(first, NoteOutcome.FailAsync, failures[0]);
                ctx.Probe.Make(second, NoteOutcome.FailAsync, failures[1]);
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>().With<NoteD>();
                }));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<AggregateException>("Both failures reach the caller together")
                .Equal("The inner exceptions are exactly the two failures", failures.Cast<Exception>().ToHashSet(), t => t.Exception.InnerExceptions.ToHashSet(),
                    (expected, actual) => expected.SetEquals(actual))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenHandlerThrowsOperationCanceledException_WhenPublishAsync_ThenPublishFailsWithOperationCanceledException(string group)
    {
        var canceling = group == "Sequence" ? nameof(NoteA) : nameof(NoteC);

        await new Scenario()
            .Given<NoteContext>($"Sequence [A, B] and Parallel [C, D], where one handler in the {group} group throws OperationCanceledException", ctx =>
            {
                ctx.Probe.Make(canceling, NoteOutcome.Cancel);
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>().With<NoteD>();
                }));
            })
            .When("ProbeNote is published and awaited", Publish)
            .ThenThrows<OperationCanceledException>("The publish fails with the cancellation, not an AggregateException")
            .RunAsync();
    }

    // ---- Token, scope and lifetime ----

    [Fact]
    public async Task GivenCancellationToken_WhenPublishAsync_ThenEveryHandlerAndDecoratorReceiveSameToken()
    {
        using var caller = new CancellationTokenSource();

        await new Scenario()
            .Given<NoteContext>("Sequence [A] and Parallel [C], each wrapped by the outer and inner token decorators", ctx =>
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>(h => h.Decorator.With<NoteOuterTokenDecorator>().With<NoteInnerTokenDecorator>());
                    x.Handler.Parallel.With<NoteC>(h => h.Decorator.With<NoteOuterTokenDecorator>().With<NoteInnerTokenDecorator>());
                })))
            .When("ProbeNote is published with the caller's token", ctx => ctx.Publisher.PublishAsync(new ProbeNote(), caller.Token))
            .Then("Two handlers and four decorators each got the caller's token")
                .Equal("Six steps saw a token", 6, ctx => ctx.Probe.Tokens.Count)
                .All("Every one of them is the caller's token", ctx => ctx.Probe.Tokens, step => Assert.Equal(caller.Token, step.Token))
            .RunAsync();
    }

    [Fact]
    public async Task GivenHandlerWithScopedDependency_WhenPublishAsync_ThenDependencyComesFromPublishersScope()
    {
        await new Scenario()
            .Given<NoteContext>("Sequence [A] and Parallel [C], both depending on the scoped ScopedMarker", ctx =>
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>();
                    x.Handler.Parallel.With<NoteC>();
                })))
            .When("ProbeNote is published through an IPublisher resolved from a scope", Publish)
            .Then("Both handlers got that scope's ScopedMarker", ctx =>
            {
                var scopes = ctx.Scope.ServiceProvider.GetRequiredService<ScopedMarker>();
                Assert.Equal(2, ctx.Probe.ScopedSeen.Count);
                Assert.All(ctx.Probe.ScopedSeen, seen => Assert.Same(scopes, seen));
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenSamePublisher_WhenPublishAsyncTwice_ThenNewHandlerInstanceIsCreatedEachTime(string group)
    {
        await new Scenario()
            .Given<NoteContext>($"Handler A in the {group} group, recording every instance constructed", ctx =>
                ctx.Build(ProbeNoteWith(x =>
                {
                    if (group == "Sequence")
                        x.Handler.Sequence.With<NoteA>();
                    else
                        x.Handler.Parallel.With<NoteA>();
                })))
            .When("ProbeNote is published twice through the same publisher", async ctx =>
            {
                await Publish(ctx);
                await Publish(ctx);
            })
            .Then("A handler was constructed for each publish")
                .Equal("Two handlers were constructed", 2, ctx => ctx.Probe.HandlersConstructed.Count)
            .And("The two publishes used different instances")
                .Distinct("The constructed handlers are different objects", ctx => ctx.Probe.HandlersConstructed, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }
}
