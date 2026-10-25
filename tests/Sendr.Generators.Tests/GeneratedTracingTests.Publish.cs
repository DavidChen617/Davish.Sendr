using System.Diagnostics;
using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

// tracing for PublishAsync through the generated publisher: PairedNote has two discovered handlers, BasedNote one,
// DecoratedNote's handlers carry decorators, and OrphanNote has none.
public partial class GeneratedTracingTests
{
    private static Func<IServiceCollection, IServiceCollection> PublisherRunningAs(string runMode) => runMode == "Parallel"
        ? services => services.AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<HandlerLibraryMarker>()))
        : services => services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));

    private static Func<IServiceCollection, IServiceCollection> DefaultPublisher => PublisherRunningAs("Sequence");

    private static Task Publish(TraceContext ctx, INotification notification) =>
        ctx.UnderCaller(() => ctx.Publisher.PublishAsync(notification, CancellationToken.None));

    private static Activity PublishSpan(SpanCollector spans, TraceContext ctx) =>
        Assert.Single(spans.InTraceOf(ctx.Caller), span => span.DisplayName.StartsWith("Publish "));

    private static Activity HandlerSpan(SpanCollector spans, TraceContext ctx, string handler) =>
        Assert.Single(spans.InTraceOf(ctx.Caller), span => span.DisplayName == $"Handle {handler}");

    [Fact]
    public async Task GivenListener_WhenPublishAsync_ThenEveryNotificationHandlerRuns()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote's two discovered handlers", ctx => ctx.Build(DefaultPublisher))
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .Then("Each handler ran exactly once")
                .Equal("X and Y each ended once", [1, 1], ctx => new[] { ctx.Notes.Count("PairedX end"), ctx.Notes.Count("PairedY end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenListenerAndOneFailingNotificationHandler_WhenPublishAsyncAwaited_ThenSameExceptionInstancePropagates()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var failure = new NoteFailure(nameof(PairedX));

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote where only X fails", ctx =>
            {
                ctx.Notes.Make(nameof(PairedX), NoteOutcome.FailAsync, failure);
                ctx.Build(DefaultPublisher);
            })
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .ThenThrows<NoteFailure>("X's exception reaches the caller")
                .True("It is the very instance X threw", thrown => ReferenceEquals(thrown.Exception, failure))
            .RunAsync();
    }

    [Fact]
    public async Task GivenOnlyListenerSamplesNone_WhenPublishAsync_ThenNoSpanIsRecordedAndHandlersRun()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.None);

        await new Scenario()
            .Given<TraceContext>("The only listener on Davish.Sendr samples every span out; PairedNote has two handlers", ctx => ctx.Build(DefaultPublisher))
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .Then("No publish or handler span was started or stopped")
                .Equal("Nothing started", 0, _ => spans.Started)
                .Empty("Nothing stopped", _ => spans.Stopped)
            .And("Every handler still ran once")
                .Equal("X and Y each ended once", [1, 1], ctx => new[] { ctx.Notes.Count("PairedX end"), ctx.Notes.Count("PairedY end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNoListenerSubscribed_WhenPublishAsync_ThenHandlersSeeCallersActivityAsCurrent()
    {
        await new Scenario()
            .Given<TraceContext>("No listener subscribed to Davish.Sendr anywhere in the process; PairedNote has two handlers", ctx =>
            {
                Assert.False(SendrActivitySource.IsEnabled, "Another listener is attached; the tracing collection should run alone.");
                ctx.Build(DefaultPublisher);
            })
            .When("PairedNote is published and awaited under the caller's Activity", ctx => Publish(ctx, new PairedNote()))
            .Then("Both handlers saw the caller's own Activity as current", ctx =>
            {
                Assert.Equal(2, ctx.Notes.Activities.Count);
                Assert.All(ctx.Notes.Activities, seen => Assert.Same(ctx.Caller, seen.Current));
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(ReturnTimings))]
    public async Task GivenListener_WhenPublishAsyncReturnsToCaller_ThenCallersActivityCurrentIsUnchanged(string timing)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var waiting = timing.Contains("waiting");

        await new Scenario()
            .Given<TraceContext>($"A listener, and BasedNote's handler, where {timing}", ctx =>
            {
                if (waiting)
                    ctx.Notes.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                else
                    ctx.Notes.CompleteSynchronously = true;
                ctx.Build(DefaultPublisher);
            })
            .When("PublishAsync returns to the caller, and Activity.Current is read before awaiting", ctx => ctx.UnderCaller(async () =>
            {
                var pending = ctx.Publisher.PublishAsync(new BasedNote(), CancellationToken.None);
                ctx.CurrentOnReturn = Activity.Current;
                ctx.Notes.Gate?.SetResult();
                await pending;
            }))
            .Then("Activity.Current is still the caller's Activity", ctx => Assert.Same(ctx.Caller, ctx.CurrentOnReturn))
            .RunAsync();
    }

    [Fact]
    public async Task GivenListenerAndNotificationHandlers_WhenPublishAsync_ThenOnePublishSpanDescribesTheNotification()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote's two handlers that succeed", ctx => ctx.Build(DefaultPublisher))
            .When("PairedNote is published and awaited under the caller's Activity", ctx => Publish(ctx, new PairedNote()))
            .Then("Exactly one stopped Internal publish span describes the notification, under the caller", ctx =>
            {
                var span = PublishSpan(spans, ctx);
                Assert.Equal(ActivityKind.Internal, span.Kind);
                Assert.Equal($"Publish {nameof(PairedNote)}", span.DisplayName);
                Assert.Equal("notification", span.GetTagItem(KindTag));
                Assert.Equal(typeof(PairedNote).FullName, span.GetTagItem(RequestTypeTag));
                Assert.Equal(ctx.Caller.SpanId, span.ParentSpanId);
                Assert.Equal(ActivityStatusCode.Unset, span.Status);
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNoHandlerDiscoveredForNotification_WhenPublishAsync_ThenNoPublishSpanIsEmitted()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and no discovered handler for OrphanNote", ctx => ctx.Build(DefaultPublisher))
            .When("OrphanNote is published and awaited under the caller's Activity", ctx => Publish(ctx, new OrphanNote()))
            .Then("No span was emitted for it")
                .Empty("The caller's trace has no span", ctx => spans.InTraceOf(ctx.Caller))
            .RunAsync();
    }

    [Fact]
    public async Task GivenSeveralNotificationHandlers_WhenPublishAsync_ThenEachHandlerHasItsOwnChildSpanUnderThePublishSpan()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote's two discovered handlers", ctx => ctx.Build(DefaultPublisher))
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .Then("Each handler has one stopped Internal span, both siblings under the publish span", ctx =>
            {
                var publish = PublishSpan(spans, ctx);
                foreach (var handler in new[] { typeof(PairedX), typeof(PairedY) })
                {
                    var span = HandlerSpan(spans, ctx, handler.Name);
                    Assert.Equal(publish.SpanId, span.ParentSpanId);
                    Assert.Equal(ActivityKind.Internal, span.Kind);
                    Assert.Equal("notification.handler", span.GetTagItem(KindTag));
                    Assert.Equal(handler.FullName, span.GetTagItem(HandlerTypeTag));
                    Assert.Equal(ActivityStatusCode.Unset, span.Status);
                }
            })
            .RunAsync();
    }

    public static TheoryData<string> RunModes => new() { "Sequence", "Parallel" };

    [Theory]
    [MemberData(nameof(RunModes))]
    public async Task GivenRunMode_WhenPublishAsync_ThenEveryHandlerSpanCarriesTheRunModeGroup(string runMode)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>($"A listener, and the generated publisher running PairedNote's handlers as {runMode}", ctx => ctx.Build(PublisherRunningAs(runMode)))
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .Then("Every handler span names the RunMode as its group", ctx =>
            {
                Assert.Equal(runMode.ToLowerInvariant(), HandlerSpan(spans, ctx, nameof(PairedX)).GetTagItem(GroupTag));
                Assert.Equal(runMode.ToLowerInvariant(), HandlerSpan(spans, ctx, nameof(PairedY)).GetTagItem(GroupTag));
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenTwoHandlersInSequenceRunMode_WhenPublishAsync_ThenTheirSpansDoNotOverlapInTime()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, RunMode Sequence, and PairedNote where X waits 50 ms before finishing", ctx =>
            {
                ctx.Notes.Delay(nameof(PairedX), TimeSpan.FromMilliseconds(50));
                ctx.Build(PublisherRunningAs("Sequence"));
            })
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .Then("One handler span ended before the other started, in either order", ctx =>
            {
                var spansInOrder = new[] { HandlerSpan(spans, ctx, nameof(PairedX)), HandlerSpan(spans, ctx, nameof(PairedY)) }
                    .OrderBy(span => span.StartTimeUtc)
                    .ToArray();
                Assert.True(spansInOrder[0].StartTimeUtc + spansInOrder[0].Duration <= spansInOrder[1].StartTimeUtc,
                    $"First ended at {spansInOrder[0].StartTimeUtc + spansInOrder[0].Duration:O}, second started at {spansInOrder[1].StartTimeUtc:O}");
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenDecoratedNotificationHandler_WhenPublishAsync_ThenHandlerSpanIsCurrentInsideTheDecorator()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and DecoratedNote's handlers carrying [DecorateWith] decorators that record Activity.Current", ctx => ctx.Build(DefaultPublisher))
            .When("DecoratedNote is published and awaited", ctx => Publish(ctx, new DecoratedNote()))
            .Then("Inside every decorator, the current Activity was a handler span under this publish", ctx =>
            {
                var handlerSpans = new[] { HandlerSpan(spans, ctx, nameof(DecoratedX)), HandlerSpan(spans, ctx, nameof(DecoratedY)) };
                var decoratorViews = ctx.Notes.Activities.Where(seen => seen.Step.EndsWith("decorator")).ToArray();
                Assert.Equal(4, decoratorViews.Length);
                Assert.All(decoratorViews, seen => Assert.Contains(seen.Current, handlerSpans));
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenOneOfSeveralHandlersFails_WhenPublishAsync_ThenOnlyItsSpanAndThePublishSpanAreError()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote where only X fails", ctx =>
            {
                ctx.Notes.Make(nameof(PairedX), NoteOutcome.FailAsync, new NoteFailure(nameof(PairedX)));
                ctx.Build(DefaultPublisher);
            })
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .ThenThrows<NoteFailure>("X's failure reaches the caller")
            .And("X's span and the publish span are Error; Y's stays Unset", thrown =>
            {
                AssertError(HandlerSpan(spans, thrown.State, nameof(PairedX)), typeof(NoteFailure));
                AssertError(PublishSpan(spans, thrown.State), typeof(NoteFailure));
                Assert.Equal(ActivityStatusCode.Unset, HandlerSpan(spans, thrown.State, nameof(PairedY)).Status);
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenSeveralHandlersFail_WhenPublishAsync_ThenPublishSpanRecordsAggregateException()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and PairedNote where both handlers fail", ctx =>
            {
                ctx.Notes.Make(nameof(PairedX), NoteOutcome.FailAsync, new NoteFailure(nameof(PairedX)));
                ctx.Notes.Make(nameof(PairedY), NoteOutcome.FailAsync, new NoteFailure(nameof(PairedY)));
                ctx.Build(DefaultPublisher);
            })
            .When("PairedNote is published and awaited", ctx => Publish(ctx, new PairedNote()))
            .ThenThrows<AggregateException>("Both failures reach the caller together")
            .And("The publish span records AggregateException, and both handler spans record their own failure", thrown =>
            {
                AssertError(PublishSpan(spans, thrown.State), typeof(AggregateException));
                AssertError(HandlerSpan(spans, thrown.State, nameof(PairedX)), typeof(NoteFailure));
                AssertError(HandlerSpan(spans, thrown.State, nameof(PairedY)), typeof(NoteFailure));
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNotificationHandlerIsCanceled_WhenPublishAsync_ThenHandlerAndPublishSpansAreMarkedError()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and BasedNote whose only handler ends with OperationCanceledException", ctx =>
            {
                ctx.Notes.Make(nameof(BasedNoteHandler), NoteOutcome.Cancel);
                ctx.Build(DefaultPublisher);
            })
            .When("BasedNote is published and awaited", ctx => Publish(ctx, new BasedNote()))
            .ThenThrows<OperationCanceledException>("The cancellation reaches the caller")
            .And("Both the handler span and the publish span are Error with OperationCanceledException", thrown =>
            {
                AssertError(HandlerSpan(spans, thrown.State, nameof(BasedNoteHandler)), typeof(OperationCanceledException));
                AssertError(PublishSpan(spans, thrown.State), typeof(OperationCanceledException));
            })
            .RunAsync();
    }
}
