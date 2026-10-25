using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// decorators scenarios that must hold for every sender and publisher. Each test project derives a class that
/// only says how its sender and publisher are registered; xUnit reports the inherited tests under that class.
/// </summary>
public abstract class DecoratorsSpec
{
    /// <summary>Registers the sender and publisher under test, plus whatever they need to reach the pipeline handlers.</summary>
    protected abstract IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services);

    [Theory]
    [MemberData(nameof(PipelineCases.SendAsyncKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenHandlerWithOneDecorator_WhenSendAsync_ThenDecoratorRunsAroundHandlerAndCallerGetsDecoratorResult(string kind)
    {
        string[] expectedLog = PipelineCases.HasResponse(kind)
            ? ["decorator before", "handler", $"decorator got {PipelineCases.HandlerResponse}", "decorator after"]
            : ["decorator before", "handler", "decorator after"];

        await new Scenario()
            .Given<PipelineContext>("A handler wrapped by one decorator that records around next() and adds 1000 to the response", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"{kind} is called and awaited", async ctx => { ctx.Result = await PipelineCases.Single[kind](ctx.Scope.ServiceProvider); })
            .Then("The decorator ran before and after the handler, and saw the handler's response")
                .Equal("decorator before, handler, decorator after", expectedLog, ctx => ctx.Probe.Log)
            .And("The caller got what the decorator passed back")
                .Equal("The response is the handler's 7 plus 1000, or none without a response",
                    PipelineCases.HasResponse(kind) ? PipelineCases.HandlerResponse + PipelineCases.DecoratorOffset : (object?)null,
                    ctx => ctx.Result)
            .RunAsync();
    }

    [Fact]
    public async Task GivenStreamHandlerWithOneDecorator_WhenEnumerated_ThenDecoratorWrapsWholeEnumeration()
    {
        await new Scenario()
            .Given<PipelineContext>("A stream handler yielding 1 and 2, wrapped by one decorator that records around the enumeration", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When("SendStream is enumerated to the end", async ctx => { ctx.Result = await PipelineCases.Single[PipelineCases.SendStream](ctx.Scope.ServiceProvider); })
            .Then("The decorator wrapped the whole enumeration")
                .Equal("decorator before, both items, decorator after", ["decorator before", "handler 1", "handler 2", "decorator after"], ctx => ctx.Probe.Log)
            .And("Every item reached the caller")
                .Equal("The items are 1, 2", (object?)new List<int> { 1, 2 }, ctx => ctx.Result, SameResult)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratorClassImplementingSeveralDecoratorInterfaces_WhenAppliedToEachHandlerKind_ThenMatchingHandleAsyncRuns(string kind)
    {
        await new Scenario()
            .Given<PipelineContext>("One decorator class implementing all seven decorator interfaces, attached to every kind of handler", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to", async ctx => { ctx.Result = await PipelineCases.Single[kind](ctx.Scope.ServiceProvider); })
            .Then("Only the interface matching that handler kind ran")
                .Equal($"Only {PipelineCases.DecoratorInterfaceFor(kind)}.HandleAsync ran", [PipelineCases.DecoratorInterfaceFor(kind)], ctx => ctx.Probe.DecoratorInterfaces)
            .And("The handler ran")
                .Equal("The handler ran once", 1, ctx => ctx.Probe.HandleCalls)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenTwoDecorators_WhenDispatched_ThenFirstDeclaredIsOutermost(string kind)
    {
        await new Scenario()
            .Given<PipelineContext>("A handler wrapped by decorator A, then decorator B", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to", async ctx => { ctx.Result = await PipelineCases.Pair[kind](ctx.Scope.ServiceProvider); })
            .Then("A, declared first, is the outermost")
                .Equal("A before, B before, handler, B after, A after", NestedLog(kind), ctx => ctx.Probe.Log)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenSameProvider_WhenDispatchedTwice_ThenDecoratorOrderIsSameEachTime(string kind)
    {
        await new Scenario()
            .Given<PipelineContext>("A handler wrapped by decorator A, then decorator B", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to twice through the same provider", async ctx =>
            {
                await PipelineCases.Pair[kind](ctx.Scope.ServiceProvider);
                await PipelineCases.Pair[kind](ctx.Scope.ServiceProvider);
            })
            .Then("Both dispatches nested the decorators the same way")
                .Equal("A before, B before, handler, B after, A after, twice", [.. NestedLog(kind), .. NestedLog(kind)], ctx => ctx.Probe.Log)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratorThatDoesNotCallNext_WhenDispatched_ThenHandlerDoesNotRunAndCallerGetsDecoratorResult(string kind)
    {
        object? expected = kind == PipelineCases.SendStream
            ? new List<int> { PipelineCases.ShortCircuitResponse }
            : PipelineCases.HasResponse(kind) ? PipelineCases.ShortCircuitResponse : null;

        await new Scenario()
            .Given<PipelineContext>("A handler wrapped by a decorator that never calls next()", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to", async ctx => { ctx.Result = await PipelineCases.Short[kind](ctx.Scope.ServiceProvider); })
            .Then("The handler's HandleAsync never ran")
                .Equal("HandleAsync was not called", 0, ctx => ctx.Probe.HandleCalls)
            .And("The caller got the decorator's own result")
                .Equal("The result is -1 (one -1 item for a stream), or none without a response", expected, ctx => ctx.Result, SameResult)
            .RunAsync();
    }

    /// <summary>
    /// For PartialNote: the decorator wrapped only the decorated handler, and the plain handler ran without it.
    /// Parallel handlers may interleave, so the plain handler's entry is checked apart from the wrapped ones.
    /// </summary>
    protected static void AssertOnlyDecoratedHandlerWasWrapped(PipelineProbe probe)
    {
        Assert.Equal(["decorator before", "decorated handler", "decorator after"], probe.Log.Where(entry => entry != "plain handler"));
        Assert.Single(probe.Log, "plain handler");
        Assert.Equal([nameof(Davish.Sendr.INotificationDecorator)], probe.DecoratorInterfaces);
    }

    private static string[] NestedLog(string kind) =>
        ["A before", "B before", .. PipelineCases.HandlerEntries(kind), "B after", "A after"];

    private static bool SameResult(object? expected, object? actual) =>
        expected is List<int> expectedItems && actual is List<int> actualItems
            ? expectedItems.SequenceEqual(actualItems)
            : Equals(expected, actual);

    /// <summary>The state every decorators and handler-lifetime scenario starts from.</summary>
    public sealed class PipelineContext
    {
        public PipelineProbe Probe { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public IServiceScope Scope { get; private set; } = null!;
        public object? Result { get; set; }

        public void Build(Func<IServiceCollection, IServiceCollection> addSenderAndPublisher)
        {
            Provider = addSenderAndPublisher(new ServiceCollection().AddSingleton(Probe)).BuildServiceProvider();
            Scope = Provider.CreateScope();
        }
    }
}
