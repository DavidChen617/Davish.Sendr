using System.Diagnostics;
using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// tracing scenarios for SendAsync, which must hold for every sender. Publish scenarios depend on the default
/// publisher's groups or the generated publisher's RunMode, so they live in the derived classes.
/// </summary>
/// <remarks>
/// Each test creates its own <see cref="SpanCollector"/> with <c>using</c> outside the scenario, so the listener
/// is gone when the test ends even if a step fails; spans are matched to the caller's Activity by trace id.
/// </remarks>
public abstract class TracingSpec
{
    protected const string KindTag = "sendr.kind";
    protected const string RequestTypeTag = "sendr.request.type";
    protected const string HandlerTypeTag = "sendr.handler.type";
    protected const string GroupTag = "sendr.notification.group";
    protected const string ErrorTypeTag = "error.type";

    /// <summary>Registers the sender under test, plus whatever it needs to reach the probe handlers.</summary>
    protected abstract IServiceCollection AddSenderUnderTest(IServiceCollection services);

    [Fact]
    public async Task GivenListenerSubscribedByLiteralSourceName_WhenSendAsync_ThenReceivesSpanFromDavishSendrSource()
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded, sourceName: "Davish.Sendr");
        var dispatch = DispatchCases.All["SendAsync(IQuery<T>)"];

        await new Scenario()
            .Given<TraceContext>("A listener subscribed by the literal source name \"Davish.Sendr\"", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When("SendAsync is called and awaited under the caller's Activity", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .Then("The listener received the send span from the Davish.Sendr source", ctx =>
            {
                var span = Assert.Single(spans.InTraceOf(ctx.Caller));
                Assert.Equal("Davish.Sendr", span.Source.Name);
                Assert.Equal(SendrActivitySource.Name, span.Source.Name);
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerSamplingAllData_WhenSendAsyncSucceeds_ThenOneStoppedInternalSpanDescribesTheMessage(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener sampling all data, and a handler that succeeds", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called and awaited", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .Then("Exactly one stopped Internal span describes the message", ctx =>
            {
                var span = Assert.Single(spans.InTraceOf(ctx.Caller));
                Assert.Equal(ActivityKind.Internal, span.Kind);
                Assert.Equal($"Send {dispatch.MessageType.Name}", span.DisplayName);
                Assert.Equal(dispatch.MessageType.FullName, span.GetTagItem(RequestTypeTag));
                Assert.Equal(dispatch.SpanKind, span.GetTagItem(KindTag));
                Assert.Equal(ActivityStatusCode.Unset, span.Status);
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerAndCallerActivity_WhenSendAsync_ThenSendSpanIsChildOfCallerAndCurrentInsideDecoratorAndHandler(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener, and a handler wrapped by two decorators that record Activity.Current", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called and awaited under the caller's Activity", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .Then("The send span is the caller's child and is current inside both decorators and the handler", ctx =>
            {
                var span = Assert.Single(spans.InTraceOf(ctx.Caller));
                Assert.Equal(ctx.Caller.SpanId, span.ParentSpanId);
                Assert.Equal(["outer decorator", "inner decorator", "handler"], ctx.Dispatch.Activities.Select(seen => seen.Step));
                Assert.All(ctx.Dispatch.Activities, seen => Assert.Same(span, seen.Current));
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerFailsAsynchronously_WhenSendAsyncAwaited_ThenSpanIsStoppedWithErrorStatusAndErrorType(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener, and a handler that fails after an await", ctx =>
            {
                ctx.Dispatch.Failure = new ProbeFailure();
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .ThenThrows<ProbeFailure>("The handler's failure reaches the caller")
            .And("The span stopped with Error status and the exception's type", thrown =>
                AssertError(Assert.Single(spans.InTraceOf(thrown.State.Caller)), typeof(ProbeFailure)))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerThrowsSynchronously_WhenSendAsyncAwaited_ThenSpanIsStoppedWithErrorStatus(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener, and a handler whose HandleAsync throws before returning a task", ctx =>
            {
                ctx.Dispatch.Failure = new ProbeFailure();
                ctx.Dispatch.ThrowSynchronously = true;
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .ThenThrows<ProbeFailure>("The handler's exception reaches the caller when awaited")
            .And("The span still stopped, with Error status and the exception's type", thrown =>
                AssertError(Assert.Single(spans.InTraceOf(thrown.State.Caller)), typeof(ProbeFailure)))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerIsCanceled_WhenSendAsyncAwaited_ThenSpanIsMarkedErrorWithCancellationType(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener, and a handler that ends with OperationCanceledException", ctx =>
            {
                ctx.Dispatch.Cancel = true;
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .ThenThrows<OperationCanceledException>("The cancellation reaches the caller")
            .And("The span is marked Error with OperationCanceledException as its type", thrown =>
                AssertError(Assert.Single(spans.InTraceOf(thrown.State.Caller)), typeof(OperationCanceledException)))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListener_WhenSendAsync_ThenHandlerResultIsReturnedUnchanged(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener sampling all data", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called with 21 and awaited", ctx => ctx.UnderCaller(async () => { ctx.Response = await dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None); }))
            .Then("The handler ran once")
                .Equal("The handler ran once", [dispatch.HandlerName], ctx => ctx.Dispatch.Handled)
            .And("The caller got the handler's answer")
                .Equal("42, or none without a response", dispatch.HasResponse ? 42 : (object?)null, ctx => ctx.Response)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerAndFailingHandler_WhenSendAsyncAwaited_ThenSameExceptionInstancePropagates(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("A listener, and a handler that fails", ctx =>
            {
                ctx.Dispatch.Failure = new ProbeFailure();
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .ThenThrows<ProbeFailure>("The handler's exception reaches the caller")
                .True("It is the very instance the handler threw, not a wrapper", thrown => ReferenceEquals(thrown.Exception, thrown.State.Dispatch.Failure))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerAndNoHandler_WhenSendAsyncAwaited_ThenThrowsInvalidOperationException(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>($"A listener, and no handler for {dispatch.OrphanType.Name}", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called with it and awaited", ctx => ctx.UnderCaller(() => dispatch.SendOrphan(ctx.Sender, CancellationToken.None)))
            .ThenThrows<InvalidOperationException>("The missing handler is still reported")
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloadsWithSynchronousFailures), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerAndNoHandlerOrSynchronouslyThrowingHandler_WhenSendAsyncCalled_ThenReturnsFaultedTaskInsteadOfThrowing(string overload, string failure)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];
        Func<ISender, Task> call = null!;
        DispatchProbe probe = null!;

        await new Scenario()
            .Given<TraceContext>($"A listener, and {failure} for the message", ctx =>
            {
                probe = ctx.Dispatch;
                call = dispatch.Failing(failure, probe);
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called, and only then is the returned task awaited", ctx => DispatchCases.CallThenAwait(() => call(ctx.Sender)))
            .Then("Nothing escaped the call itself, and the task carries the failure, as without a listener", outcome =>
            {
                Assert.Null(outcome.FromCall);
                if (failure == DispatchCases.NoHandler)
                    Assert.IsType<InvalidOperationException>(outcome.FromAwait);
                else
                    Assert.Same(probe.Failure, outcome.FromAwait);
            })
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenListenerAndNoHandler_WhenSendAsyncAwaited_ThenSpanIsStoppedWithErrorStatus(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>($"A listener, and no handler for {dispatch.OrphanType.Name}", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called with it and awaited", ctx => ctx.UnderCaller(() => dispatch.SendOrphan(ctx.Sender, CancellationToken.None)))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
            .And("One span for the message stopped, with Error status and InvalidOperationException as error.type", thrown =>
                AssertError(Assert.Single(spans.InTraceOf(thrown.State.Caller)), typeof(InvalidOperationException)))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenOnlyListenerSamplesNone_WhenSendAsync_ThenNoSpanIsRecordedAndResultIsReturned(string overload)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.None);
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("The only listener on Davish.Sendr samples every span out", ctx => ctx.BuildSender(AddSenderUnderTest))
            .When($"{overload} is called with 21 and awaited under the caller's Activity", ctx => ctx.UnderCaller(async () => { ctx.Response = await dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None); }))
            .Then("No span was started or stopped")
                .Equal("Nothing started", 0, _ => spans.Started)
                .Empty("Nothing stopped", _ => spans.Stopped)
            .And("The handler still saw the caller's Activity as current", ctx => Assert.Same(ctx.Caller, Assert.Single(ctx.Dispatch.Activities, seen => seen.Step == "handler").Current))
            .And("The caller got the usual result")
                .Equal("42, or none without a response", dispatch.HasResponse ? 42 : (object?)null, ctx => ctx.Response)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenNoListenerSubscribed_WhenSendAsync_ThenHandlerSeesCallersActivityAsCurrent(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<TraceContext>("No listener subscribed to Davish.Sendr anywhere in the process", ctx =>
            {
                Assert.False(SendrActivitySource.IsEnabled, "Another listener is attached; the tracing collection should run alone.");
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited under the caller's Activity", ctx => ctx.UnderCaller(() => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None)))
            .Then("The handler saw the caller's own Activity as current", ctx =>
                Assert.Same(ctx.Caller, Assert.Single(ctx.Dispatch.Activities, seen => seen.Step == "handler").Current))
            .RunAsync();
    }

    public static TheoryData<string> ReturnTimings => new() { "the handler completes synchronously", "the handler is still waiting on an outside signal" };

    [Theory]
    [MemberData(nameof(ReturnTimings))]
    public async Task GivenListener_WhenSendAsyncReturnsToCaller_ThenCallersActivityCurrentIsUnchanged(string timing)
    {
        using var spans = new SpanCollector(ActivitySamplingResult.AllDataAndRecorded);
        var dispatch = DispatchCases.All["SendAsync(IQuery<T>)"];
        var waiting = timing.Contains("waiting");

        await new Scenario()
            .Given<TraceContext>($"A listener, where {timing}", ctx =>
            {
                if (waiting)
                    ctx.Dispatch.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                else
                    ctx.Dispatch.CompleteSynchronously = true;
                ctx.BuildSender(AddSenderUnderTest);
            })
            .When("SendAsync returns to the caller, and Activity.Current is read before awaiting", ctx => ctx.UnderCaller(async () =>
            {
                var pending = dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None);
                ctx.CurrentOnReturn = Activity.Current;
                ctx.Dispatch.Gate?.SetResult();
                await pending;
            }))
            .Then("Activity.Current is still the caller's Activity", ctx => Assert.Same(ctx.Caller, ctx.CurrentOnReturn))
            .RunAsync();
    }

    protected static void AssertError(Activity span, Type exceptionType)
    {
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(exceptionType.FullName, span.GetTagItem(ErrorTypeTag));
    }

    /// <summary>The state every tracing scenario starts from.</summary>
    public sealed class TraceContext
    {
        public DispatchProbe Dispatch { get; } = new();
        public NotificationProbe Notes { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public IServiceScope Scope { get; private set; } = null!;
        public ISender Sender => Scope.ServiceProvider.GetRequiredService<ISender>();
        public IPublisher Publisher => Scope.ServiceProvider.GetRequiredService<IPublisher>();

        /// <summary>The caller's Activity, started for the duration of the When step.</summary>
        public Activity Caller { get; private set; } = null!;

        public object? Response { get; set; }
        public Activity? CurrentOnReturn { get; set; }

        public void BuildSender(Func<IServiceCollection, IServiceCollection> addSender) => Build(addSender);

        public void Build(Func<IServiceCollection, IServiceCollection> add)
        {
            Provider = add(new ServiceCollection().AddDispatchProbe(Dispatch).AddSingleton(Notes)).BuildServiceProvider();
            Scope = Provider.CreateScope();
        }

        /// <summary>Runs <paramref name="call"/> inside the caller's own Activity, and stops it afterwards.</summary>
        public async Task UnderCaller(Func<Task> call)
        {
            using var caller = SpanCollector.StartCaller();
            Caller = caller;
            await call();
        }
    }
}
