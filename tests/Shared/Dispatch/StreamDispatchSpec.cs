using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// stream-dispatch scenarios that must hold for every sender. Each test project derives a class that only
/// says how its sender is registered; xUnit reports the inherited tests under that derived class.
/// </summary>
public abstract class StreamDispatchSpec
{
    /// <summary>Registers the sender under test, plus whatever it needs to reach the probe handlers.</summary>
    protected abstract IServiceCollection AddSenderUnderTest(IServiceCollection services);

    [Fact]
    public async Task GivenStreamHandler_WhenEnumerated_ThenItemsArriveInHandlerOrder()
    {
        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler, which yields 1, 2, 3 for a count of 3", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is enumerated to the end", ctx => ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(3), CancellationToken.None)))
            .Then("The caller gets exactly the handler's items, in order")
                .Equal("The items are 1, 2, 3", [1, 2, 3], ctx => ctx.Items)
            .RunAsync();
    }

    [Fact]
    public async Task GivenStreamRequestTypeWithTwoItemTypes_WhenEachEnumerated_ThenEachGetsItsOwnHandlerItems()
    {
        await new Scenario()
            .Given<StreamContext>("DualStream, with one handler yielding ints and another yielding \"#n\" strings", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is enumerated once as int and once as string", async ctx =>
            {
                await ctx.EnumerateAll(ctx.StreamSender.SendStream<int>(new DualStream(2), CancellationToken.None));
                await ctx.EnumerateAll(ctx.StreamSender.SendStream<string>(new DualStream(2), CancellationToken.None));
            })
            .Then("Each enumeration got only its own handler's items")
                .Equal("The int run yielded 1, 2 and the string run yielded #1, #2", [1, 2, "#1", "#2"], ctx => ctx.Items)
            .RunAsync();
    }

    public static TheoryData<string> StaticTypes => new() { "interface", "base type" };

    [Theory]
    [MemberData(nameof(StaticTypes))]
    public async Task GivenStreamRequestStaticallyTypedAsInterfaceOrBaseType_WhenEnumerated_ThenHandlerForRuntimeTypeRuns(string staticType)
    {
        await new Scenario()
            .Given<StreamContext>("A stream handler registered for the concrete ProbeStream type", ctx => ctx.Build(AddSenderUnderTest))
            .When($"The request is sent through a variable typed as its {staticType} and enumerated", ctx =>
            {
                IStreamRequest<int> asInterface = new ProbeStream(3);
                ProbeStreamBase asBase = new ProbeStream(3);
                return ctx.EnumerateAll(staticType == "interface"
                    ? ctx.StreamSender.SendStream(asInterface, CancellationToken.None)
                    : ctx.StreamSender.SendStream(asBase, CancellationToken.None));
            })
            .Then("The concrete type's handler produced the items")
                .Equal("ProbeStreamHandler ran once", [nameof(ProbeStreamHandler)], ctx => ctx.Probe.Handled)
                .Equal("The items are 1, 2, 3", [1, 2, 3], ctx => ctx.Items)
            .RunAsync();
    }

    [Fact]
    public void GivenStreamHandlerAndDecorator_WhenSendStreamNotYetEnumerated_ThenNeitherIsResolvedNorRun()
    {
        new Scenario()
            .Given<StreamContext>("ProbeStreamHandler wrapped by decorators, all recording when they are constructed or run", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is called but the sequence is never enumerated", ctx => { ctx.StreamSender.SendStream(new ProbeStream(3), CancellationToken.None); })
            .Then("No handler was constructed")
                .Empty("No handler instance exists", ctx => ctx.Probe.HandlersConstructed)
            .And("No decorator was constructed")
                .Empty("No decorator instance exists", ctx => ctx.Probe.DecoratorsConstructed)
            .And("Nothing ran")
                .Empty("Neither the handler nor a decorator saw a token", ctx => ctx.Probe.Tokens)
            .Run();
    }

    [Fact]
    public void GivenNullStreamRequest_WhenSendStream_ThenThrowsArgumentNullExceptionImmediately()
    {
        new Scenario()
            .Given<StreamContext>("A sender", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is called with null and the result is not enumerated", ctx => { ctx.StreamSender.SendStream<int>(null!, CancellationToken.None); })
            .ThenThrows<ArgumentNullException>("The call itself rejects the null request")
                .Equal("The parameter is named 'request'", "request", thrown => thrown.Exception.ParamName)
            .Run();
    }

    [Fact]
    public async Task GivenNoStreamHandler_WhenSendStreamThenFirstMoveNext_ThenOnlyMoveNextThrowsInvalidOperationException()
    {
        await new Scenario()
            .Given<StreamContext>("A sender with no handler for OrphanStream", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is called, then MoveNextAsync is called once on the result", async ctx =>
            {
                var sequence = ctx.StreamSender.SendStream(new OrphanStream(), CancellationToken.None);
                ctx.SendStreamReturned = true;
                await sequence.GetAsyncEnumerator().MoveNextAsync();
            })
            .ThenThrows<InvalidOperationException>("The first MoveNextAsync reports the missing handler")
                .True("SendStream itself returned without throwing", thrown => thrown.State.SendStreamReturned)
            .RunAsync();
    }

    [Fact]
    public async Task GivenStreamHandlerThrowsMidEnumeration_WhenEnumerated_ThenEarlierItemsArriveAndOriginalExceptionPropagates()
    {
        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler yields two items and then fails with a ProbeFailure", ctx =>
            {
                ctx.Probe.Failure = new ProbeFailure();
                ctx.Probe.FailAfterItems = 2;
                ctx.Build(AddSenderUnderTest);
            })
            .When("SendStream is enumerated", ctx => ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(5), CancellationToken.None)))
            .ThenThrows<ProbeFailure>("The handler's exception reaches the caller")
                .Equal("The two items before the failure arrived first", [1, 2], thrown => thrown.State.Items)
                .True("It is the very instance the handler threw, not a wrapper", thrown => ReferenceEquals(thrown.Exception, thrown.State.Probe.Failure))
            .RunAsync();
    }

    public static TheoryData<string> CancellationModes => new()
    {
        "token passed to SendStream",
        "token given with WithCancellation",
        "both tokens, SendStream's canceled",
        "both tokens, WithCancellation's canceled",
    };

    [Theory]
    [MemberData(nameof(CancellationModes))]
    public async Task GivenCancelableToken_WhenTokenCanceledDuringEnumeration_ThenEnumerationObservesCancellation(string mode)
    {
        using var sendStreamSource = new CancellationTokenSource();
        using var enumeratorSource = new CancellationTokenSource();
        var passToSendStream = mode != "token given with WithCancellation";
        var passToEnumerator = mode != "token passed to SendStream";
        var toCancel = mode is "token given with WithCancellation" or "both tokens, WithCancellation's canceled" ? enumeratorSource : sendStreamSource;

        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler checks its token before every item", ctx => ctx.Build(AddSenderUnderTest))
            .When($"With the {mode}, the token is canceled after the first item and enumeration continues", async ctx =>
            {
                var sequence = ctx.StreamSender.SendStream(new ProbeStream(3), passToSendStream ? sendStreamSource.Token : CancellationToken.None);
                await using var enumerator = sequence.GetAsyncEnumerator(passToEnumerator ? enumeratorSource.Token : CancellationToken.None);
                await enumerator.MoveNextAsync();
                ctx.Items.Add(enumerator.Current);
                await toCancel.CancelAsync();
                await enumerator.MoveNextAsync();
            })
            .ThenThrows<OperationCanceledException>("The next MoveNextAsync fails with the cancellation")
                .Equal("Only the item before the cancellation arrived", [1], thrown => thrown.State.Items)
            .RunAsync();
    }

    [Fact]
    public async Task GivenTokenPassedToSendStream_WhenEnumerated_ThenHandlerAndEveryDecoratorReceiveSameToken()
    {
        using var caller = new CancellationTokenSource();

        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler wrapped by two decorators, all recording the token they get", ctx => ctx.Build(AddSenderUnderTest))
            .When("Only SendStream gets the caller's token, and the result is enumerated", ctx =>
                ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(1), caller.Token)))
            .Then("Every step of the pipeline got the caller's token, outermost first")
                .Equal("outer decorator, inner decorator and handler each saw the caller's token",
                    [("outer decorator", caller.Token), ("inner decorator", caller.Token), ("handler", caller.Token)],
                    ctx => ctx.Probe.Tokens)
            .RunAsync();
    }

    [Fact]
    public async Task GivenStreamHandlerWithScopedDependency_WhenEnumerated_ThenDependencyComesFromSendersScope()
    {
        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler depends on the scoped ScopedMarker", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream on an IStreamSender resolved from a scope is enumerated", ctx =>
                ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(1), CancellationToken.None)))
            .Then("The handler got that scope's ScopedMarker", ctx =>
                Assert.Same(ctx.Scope.ServiceProvider.GetRequiredService<ScopedMarker>(), Assert.Single(ctx.Probe.ScopedSeen)))
            .RunAsync();
    }

    [Fact]
    public async Task GivenSameSender_WhenStreamEnumeratedTwice_ThenNewHandlerInstanceIsCreatedEachTime()
    {
        await new Scenario()
            .Given<StreamContext>("ProbeStreamHandler records every instance that is constructed", ctx => ctx.Build(AddSenderUnderTest))
            .When("SendStream is called and enumerated twice on the same sender", async ctx =>
            {
                await ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(1), CancellationToken.None));
                await ctx.EnumerateAll(ctx.StreamSender.SendStream(new ProbeStream(1), CancellationToken.None));
            })
            .Then("A handler was constructed for each enumeration")
                .Equal("Two handlers were constructed", 2, ctx => ctx.Probe.HandlersConstructed.Count)
            .And("The two enumerations used different instances")
                .Distinct("The constructed handlers are different objects", ctx => ctx.Probe.HandlersConstructed, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }

    /// <summary>The state every stream-dispatch scenario starts from.</summary>
    public sealed class StreamContext
    {
        public DispatchProbe Probe { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public IServiceScope Scope { get; private set; } = null!;
        public IStreamSender StreamSender { get; private set; } = null!;
        public List<object> Items { get; } = [];
        public bool SendStreamReturned { get; set; }

        public void Build(Func<IServiceCollection, IServiceCollection> addSender)
        {
            Provider = addSender(new ServiceCollection().AddDispatchProbe(Probe)).BuildServiceProvider();
            Scope = Provider.CreateScope();
            StreamSender = Scope.ServiceProvider.GetRequiredService<IStreamSender>();
        }

        /// <summary>Enumerates to the end, keeping every item that arrived even if enumeration then fails.</summary>
        public async Task EnumerateAll<TItem>(IAsyncEnumerable<TItem> sequence)
        {
            await foreach (var item in sequence)
                Items.Add(item!);
        }
    }
}
