using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// request-dispatch scenarios that must hold for every sender. Each test project derives a class that only
/// says how its sender is registered; xUnit reports the inherited tests under that derived class.
/// </summary>
public abstract class RequestDispatchSpec
{
    /// <summary>Registers the sender under test, plus whatever it needs to reach the probe handlers.</summary>
    protected abstract IServiceCollection AddSenderUnderTest(IServiceCollection services);

    [Theory]
    [MemberData(nameof(DispatchCases.WithoutResponse), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerForMessageWithoutResponse_WhenSendAsync_ThenHandlerRunsAndTaskCompletes(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"A sender that can reach {dispatch.HandlerName}, which records each call", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called and awaited", async ctx => { ctx.Response = await dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None); })
            .Then("The awaited task completed and the handler ran exactly once")
                .Equal("The handler ran once", [dispatch.HandlerName], ctx => ctx.Probe.Handled)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.WithResponse), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerForMessageWithResponse_WhenSendAsync_ThenHandlerResponseIsReturned(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"A sender that can reach {dispatch.HandlerName}, which answers twice the request value", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with 21 and awaited", ctx => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None))
            .Then("The caller gets the handler's answer")
                .Equal("The response is 42", (object?)42, response => response)
            .RunAsync();
    }

    public static TheoryData<string> DualResponseOverloads => new(DualSends.Keys);

    private static readonly IReadOnlyDictionary<string, (Func<ISender, int, Task<object>> AsInt, Func<ISender, int, Task<object>> AsString)> DualSends =
        new Dictionary<string, (Func<ISender, int, Task<object>>, Func<ISender, int, Task<object>>)>
        {
            ["SendAsync(IRequest<T>)"] = (
                async (s, v) => await s.SendAsync<int>(new DualRequest(v), CancellationToken.None),
                async (s, v) => await s.SendAsync<string>(new DualRequest(v), CancellationToken.None)),
            ["SendAsync(ICommand<T>)"] = (
                async (s, v) => await s.SendAsync<int>(new DualCommand(v), CancellationToken.None),
                async (s, v) => await s.SendAsync<string>(new DualCommand(v), CancellationToken.None)),
            ["SendAsync(IQuery<T>)"] = (
                async (s, v) => await s.SendAsync<int>(new DualQuery(v), CancellationToken.None),
                async (s, v) => await s.SendAsync<string>(new DualQuery(v), CancellationToken.None)),
        };

    [Theory]
    [MemberData(nameof(DualResponseOverloads))]
    public async Task GivenMessageTypeWithTwoResponseTypes_WhenBothSentConcurrently_ThenEachGetsItsOwnHandlerResult(string overload)
    {
        var send = DualSends[overload];

        await new Scenario()
            .Given<DispatchContext>("A message type answering both int and string, with one handler per response type", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is sent concurrently, alternating int and string responses", ctx =>
                Task.WhenAll(Enumerable.Range(1, 20).Select(v => v % 2 == 0 ? send.AsInt(ctx.Sender, v) : send.AsString(ctx.Sender, v))))
            .Then("Every call got the answer of the handler for its response type")
                .Equal("Even values came back as int, odd values as \"#value\" strings",
                    Enumerable.Range(1, 20).Select(v => v % 2 == 0 ? (object)v : $"#{v}").ToArray(),
                    results => results)
            .RunAsync();
    }

    public static TheoryData<string, string> OverloadsByStaticType
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var overload in DispatchCases.All.Keys)
            {
                data.Add(overload, "interface");
                data.Add(overload, "base type");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OverloadsByStaticType))]
    public async Task GivenMessageStaticallyTypedAsInterfaceOrBaseType_WhenSendAsync_ThenHandlerForRuntimeTypeRuns(string overload, string staticType)
    {
        var dispatch = DispatchCases.All[overload];
        var send = staticType == "interface" ? dispatch.SendAsInterface : dispatch.SendAsBaseType;

        await new Scenario()
            .Given<DispatchContext>($"A handler registered for the concrete message type behind {overload}", ctx => ctx.Build(AddSenderUnderTest))
            .When($"The message is sent through a variable typed as its {staticType}", async ctx => { ctx.Response = await send(ctx.Sender, 21, CancellationToken.None); })
            .Then("The concrete type's handler ran")
                .Equal("The handler for the runtime type ran once", [dispatch.HandlerName], ctx => ctx.Probe.Handled)
            .And("Its answer reached the caller")
                .Equal("The response is the handler's answer, or none for a message without a response",
                    dispatch.HasResponse ? 42 : (object?)null, ctx => ctx.Response)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenNullMessage_WhenSendAsync_ThenThrowsArgumentNullExceptionWithParamName(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>("A sender", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with null", ctx => dispatch.SendNull(ctx.Sender, CancellationToken.None))
            .ThenThrows<ArgumentNullException>("The null message is rejected")
                .Equal($"The parameter is named '{dispatch.NullParamName}'", dispatch.NullParamName, thrown => thrown.Exception.ParamName)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenNoHandlerForMessage_WhenSendAsyncAwaited_ThenThrowsInvalidOperationException(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"A sender with no handler for {dispatch.OrphanType.Name}", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with it and awaited", ctx => dispatch.SendOrphan(ctx.Sender, CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The missing handler is reported")
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerThrows_WhenSendAsyncAwaited_ThenOriginalExceptionPropagatesUnwrapped(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"{dispatch.HandlerName} fails with a ProbeFailure", ctx =>
            {
                ctx.Probe.Failure = new ProbeFailure();
                ctx.Build(AddSenderUnderTest);
            })
            .When($"{overload} is called and awaited", ctx => dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None))
            .ThenThrows<ProbeFailure>("The handler's exception reaches the caller")
                .True("It is the very instance the handler threw, not a wrapper", thrown => ReferenceEquals(thrown.Exception, thrown.State.Probe.Failure))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenCancellationToken_WhenSendAsync_ThenHandlerAndEveryDecoratorReceiveSameToken(string overload)
    {
        var dispatch = DispatchCases.All[overload];
        using var caller = new CancellationTokenSource();

        await new Scenario()
            .Given<DispatchContext>($"{dispatch.HandlerName} wrapped by two decorators, all recording the token they get", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called with the caller's token", async ctx => { ctx.Response = await dispatch.SendConcrete(ctx.Sender, 21, caller.Token); })
            .Then("Every step of the pipeline got the caller's token, outermost first")
                .Equal("outer decorator, inner decorator and handler each saw the caller's token",
                    [("outer decorator", caller.Token), ("inner decorator", caller.Token), ("handler", caller.Token)],
                    ctx => ctx.Probe.Tokens)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenHandlerWithScopedDependency_WhenSendAsync_ThenDependencyComesFromSendersScope(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"{dispatch.HandlerName} depends on the scoped ScopedMarker", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called on a sender resolved from a scope", async ctx => { ctx.Response = await dispatch.SendConcrete(ctx.Sender, 21, CancellationToken.None); })
            .Then("The handler got that scope's ScopedMarker", ctx =>
                Assert.Same(ctx.Scope.ServiceProvider.GetRequiredService<ScopedMarker>(), Assert.Single(ctx.Probe.ScopedSeen)))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DispatchCases.AllOverloads), MemberType = typeof(DispatchCases))]
    public async Task GivenSameSender_WhenSendAsyncTwice_ThenNewHandlerInstanceIsCreatedEachTime(string overload)
    {
        var dispatch = DispatchCases.All[overload];

        await new Scenario()
            .Given<DispatchContext>($"{dispatch.HandlerName} records every instance that is constructed", ctx => ctx.Build(AddSenderUnderTest))
            .When($"{overload} is called twice on the same sender", async ctx =>
            {
                await dispatch.SendConcrete(ctx.Sender, 1, CancellationToken.None);
                await dispatch.SendConcrete(ctx.Sender, 2, CancellationToken.None);
            })
            .Then("A handler was constructed for each dispatch")
                .Equal("Two handlers were constructed", 2, ctx => ctx.Probe.HandlersConstructed.Count)
            .And("The two dispatches used different instances")
                .Distinct("The constructed handlers are different objects", ctx => ctx.Probe.HandlersConstructed, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }

    /// <summary>The state every request-dispatch scenario starts from.</summary>
    public sealed class DispatchContext
    {
        public DispatchProbe Probe { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public IServiceScope Scope { get; private set; } = null!;
        public ISender Sender { get; private set; } = null!;
        public object? Response { get; set; }

        public void Build(Func<IServiceCollection, IServiceCollection> addSender)
        {
            Provider = addSender(new ServiceCollection().AddDispatchProbe(Probe)).BuildServiceProvider();
            Scope = Provider.CreateScope();
            Sender = Scope.ServiceProvider.GetRequiredService<ISender>();
        }
    }
}
