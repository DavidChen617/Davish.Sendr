using System.Diagnostics;
using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFixtures;

namespace Sendr.Tests;

// tracing for PublishAsync through the default publisher: ProbeNote's handlers go in whichever group each test needs.
public partial class TracingTests
{
    private static Func<IServiceCollection, IServiceCollection> ProbeNoteWith(Action<NotificationHandlerOptions<ProbeNote>> configure) =>
        services => services.AddSendrNotification().AddNotificationHandler(configure);

    private static void SequenceAThenParallelC(NotificationHandlerOptions<ProbeNote> x)
    {
        x.Handler.Sequence.With<NoteA>();
        x.Handler.Parallel.With<NoteC>();
    }

    private static Task PublishProbeNote(TraceContext ctx) =>
        ctx.UnderCaller(() => ctx.Publisher.PublishAsync(new ProbeNote(), CancellationToken.None));

    private static Activity PublishSpan(SpanCollector spans, TraceContext ctx) =>
        Assert.Single(spans.InTraceOf(ctx.Caller), span => span.DisplayName.StartsWith("Publish "));

    private static Activity HandlerSpan(SpanCollector spans, TraceContext ctx, string handler) =>
        Assert.Single(spans.InTraceOf(ctx.Caller), span => span.DisplayName == $"Handle {handler}");

    [Fact]
    public async Task GivenListener_WhenPublishAsync_ThenEveryNotificationHandlerRuns()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote with A in the Sequence group and C in the Parallel group", ctx => ctx.Build(ProbeNoteWith(SequenceAThenParallelC)))
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("Each handler ran exactly once")
                .Equal("A and C each ended once", [1, 1], ctx => new[] { ctx.Notes.Count("NoteA end"), ctx.Notes.Count("NoteC end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenListenerAndOneFailingNotificationHandler_WhenPublishAsyncAwaited_ThenSameExceptionInstancePropagates()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var failure = new NoteFailure(nameof(NoteA));

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote whose only handler A fails", ctx =>
            {
                ctx.Notes.Make(nameof(NoteA), NoteOutcome.FailAsync, failure);
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>()));
            })
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .ThenThrows<NoteFailure>("The handler's exception reaches the caller")
                .True("It is the very instance the handler threw", thrown => ReferenceEquals(thrown.Exception, failure))
            .RunAsync();
    }

    [Fact]
    public async Task GivenOnlyListenerSamplesNone_WhenPublishAsync_ThenNoSpanIsRecordedAndHandlersRun()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.None);

        await new Scenario()
            .Given<TraceContext>("The only listener on Davish.Sendr samples every span out; ProbeNote has A (Sequence) and C (Parallel)", ctx => ctx.Build(ProbeNoteWith(SequenceAThenParallelC)))
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("No publish or handler span was started or stopped")
                .Equal("Nothing started", 0, _ => spans.Started)
                .Empty("Nothing stopped", _ => spans.Stopped)
            .And("Every handler still ran once")
                .Equal("A and C each ended once", [1, 1], ctx => new[] { ctx.Notes.Count("NoteA end"), ctx.Notes.Count("NoteC end") })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNoListenerSubscribed_WhenPublishAsync_ThenHandlersSeeCallersActivityAsCurrent()
    {
        await new Scenario()
            .Given<TraceContext>("No listener subscribed to Davish.Sendr anywhere in the process; ProbeNote has A (Sequence) and C (Parallel)", ctx =>
            {
                Assert.False(SendrActivitySource.IsEnabled, "Another listener is attached; the tracing collection should run alone.");
                ctx.Build(ProbeNoteWith(SequenceAThenParallelC));
            })
            .When("ProbeNote is published and awaited under the caller's Activity", PublishProbeNote)
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
            .Given<TraceContext>($"A listener, and ProbeNote's handler A, where {timing}", ctx =>
            {
                if (waiting)
                    ctx.Notes.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                else
                    ctx.Notes.CompleteSynchronously = true;
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>()));
            })
            .When("PublishAsync returns to the caller, and Activity.Current is read before awaiting", ctx => ctx.UnderCaller(async () =>
            {
                var pending = ctx.Publisher.PublishAsync(new ProbeNote(), CancellationToken.None);
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
            .Given<TraceContext>("A listener, and ProbeNote with handlers that succeed", ctx => ctx.Build(ProbeNoteWith(SequenceAThenParallelC)))
            .When("ProbeNote is published and awaited under the caller's Activity", PublishProbeNote)
            .Then("Exactly one stopped Internal publish span describes the notification, under the caller", ctx =>
            {
                var span = PublishSpan(spans, ctx);
                Assert.Equal(ActivityKind.Internal, span.Kind);
                Assert.Equal($"Publish {nameof(ProbeNote)}", span.DisplayName);
                Assert.Equal("notification", span.GetTagItem(KindTag));
                Assert.Equal(typeof(ProbeNote).FullName, span.GetTagItem(RequestTypeTag));
                Assert.Equal(ctx.Caller.SpanId, span.ParentSpanId);
                Assert.Equal(ActivityStatusCode.Unset, span.Status);
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNoHandlerRegisteredForNotification_WhenPublishAsync_ThenNoPublishSpanIsEmitted()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and no AddNotificationHandler call for OrphanNote", ctx => ctx.Build(ProbeNoteWith(SequenceAThenParallelC)))
            .When("OrphanNote is published and awaited under the caller's Activity", ctx =>
                ctx.UnderCaller(() => ctx.Publisher.PublishAsync(new OrphanNote(), CancellationToken.None)))
            .Then("No span was emitted for it")
                .Empty("The caller's trace has no span", ctx => spans.InTraceOf(ctx.Caller))
            .RunAsync();
    }

    [Fact]
    public async Task GivenSeveralNotificationHandlers_WhenPublishAsync_ThenEachHandlerHasItsOwnChildSpanUnderThePublishSpan()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote with A and B in the Sequence group and C in the Parallel group", ctx =>
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>();
                })))
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("Each handler has one stopped Internal span, all siblings under the publish span", ctx =>
            {
                var publish = PublishSpan(spans, ctx);
                foreach (var handler in new[] { typeof(NoteA), typeof(NoteB), typeof(NoteC) })
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

    [Fact]
    public async Task GivenSequenceAndParallelHandlers_WhenPublishAsync_ThenEachHandlerSpanCarriesItsGroup()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote with A in the Sequence group and C in the Parallel group", ctx => ctx.Build(ProbeNoteWith(SequenceAThenParallelC)))
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("Each handler span names the group its handler belongs to", ctx =>
            {
                Assert.Equal("sequence", HandlerSpan(spans, ctx, nameof(NoteA)).GetTagItem(GroupTag));
                Assert.Equal("parallel", HandlerSpan(spans, ctx, nameof(NoteC)).GetTagItem(GroupTag));
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenTwoSequenceHandlers_WhenPublishAsync_ThenTheirSpansDoNotOverlapInTime()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and a Sequence group of A then B, where A waits 50 ms before finishing", ctx =>
            {
                ctx.Notes.Delay(nameof(NoteA), TimeSpan.FromMilliseconds(50));
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>().With<NoteB>()));
            })
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("B's span started only after A's span ended", ctx =>
            {
                var a = HandlerSpan(spans, ctx, nameof(NoteA));
                var b = HandlerSpan(spans, ctx, nameof(NoteB));
                Assert.True(a.StartTimeUtc + a.Duration <= b.StartTimeUtc, $"A ended at {a.StartTimeUtc + a.Duration:O}, B started at {b.StartTimeUtc:O}");
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenDecoratedNotificationHandler_WhenPublishAsync_ThenHandlerSpanIsCurrentInsideTheDecorator()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and handler A wrapped by a decorator that records Activity.Current", ctx =>
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>(h => h.Decorator.With<NoteOuterTokenDecorator>()))))
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .Then("Inside the decorator, the current Activity was A's handler span", ctx =>
                Assert.Same(HandlerSpan(spans, ctx, nameof(NoteA)), Assert.Single(ctx.Notes.Activities, seen => seen.Step == "outer decorator").Current))
            .RunAsync();
    }

    [Fact]
    public async Task GivenOneOfSeveralHandlersFails_WhenPublishAsync_ThenOnlyItsSpanAndThePublishSpanAreError()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote with A and B (Sequence) and C (Parallel), where only A fails", ctx =>
            {
                ctx.Notes.Make(nameof(NoteA), NoteOutcome.FailAsync, new NoteFailure(nameof(NoteA)));
                ctx.Build(ProbeNoteWith(x =>
                {
                    x.Handler.Sequence.With<NoteA>().With<NoteB>();
                    x.Handler.Parallel.With<NoteC>();
                }));
            })
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .ThenThrows<NoteFailure>("A's failure reaches the caller")
            .And("A's span and the publish span are Error; the others stay Unset", thrown =>
            {
                var ctx = thrown.State;
                AssertError(HandlerSpan(spans, ctx, nameof(NoteA)), typeof(NoteFailure));
                AssertError(PublishSpan(spans, ctx), typeof(NoteFailure));
                Assert.Equal(ActivityStatusCode.Unset, HandlerSpan(spans, ctx, nameof(NoteB)).Status);
                Assert.Equal(ActivityStatusCode.Unset, HandlerSpan(spans, ctx, nameof(NoteC)).Status);
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenSeveralHandlersFail_WhenPublishAsync_ThenPublishSpanRecordsAggregateException()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote with A (Sequence) and C (Parallel), both failing", ctx =>
            {
                ctx.Notes.Make(nameof(NoteA), NoteOutcome.FailAsync, new NoteFailure(nameof(NoteA)));
                ctx.Notes.Make(nameof(NoteC), NoteOutcome.FailAsync, new NoteFailure(nameof(NoteC)));
                ctx.Build(ProbeNoteWith(SequenceAThenParallelC));
            })
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .ThenThrows<AggregateException>("Both failures reach the caller together")
            .And("The publish span records AggregateException, and both handler spans record their own failure", thrown =>
            {
                var ctx = thrown.State;
                AssertError(PublishSpan(spans, ctx), typeof(AggregateException));
                AssertError(HandlerSpan(spans, ctx, nameof(NoteA)), typeof(NoteFailure));
                AssertError(HandlerSpan(spans, ctx, nameof(NoteC)), typeof(NoteFailure));
            })
            .RunAsync();
    }

    [Fact]
    public async Task GivenNotificationHandlerIsCanceled_WhenPublishAsync_ThenHandlerAndPublishSpansAreMarkedError()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);

        await new Scenario()
            .Given<TraceContext>("A listener, and ProbeNote whose only handler A ends with OperationCanceledException", ctx =>
            {
                ctx.Notes.Make(nameof(NoteA), NoteOutcome.Cancel);
                ctx.Build(ProbeNoteWith(x => x.Handler.Sequence.With<NoteA>()));
            })
            .When("ProbeNote is published and awaited", PublishProbeNote)
            .ThenThrows<OperationCanceledException>("The cancellation reaches the caller")
            .And("Both the handler span and the publish span are Error with OperationCanceledException", thrown =>
            {
                AssertError(HandlerSpan(spans, thrown.State, nameof(NoteA)), typeof(OperationCanceledException));
                AssertError(PublishSpan(spans, thrown.State), typeof(OperationCanceledException));
            })
            .RunAsync();
    }
}
