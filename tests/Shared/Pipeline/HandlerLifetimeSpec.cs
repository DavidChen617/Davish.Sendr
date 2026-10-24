using Davish.JustBdd;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// handler-lifetime scenarios that must hold for every sender and publisher. Each test project derives a class
/// that only says how its sender and publisher are registered; xUnit reports the inherited tests under that class.
/// </summary>
public abstract class HandlerLifetimeSpec
{
    /// <summary>Registers the sender and publisher under test, plus whatever they need to reach the pipeline handlers.</summary>
    protected abstract IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services);

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDisposableHandlerWithoutDecorator_WhenScopeEnds_ThenHandlerIsDisposedExactlyOnce(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A disposable handler with no decorator", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope ends", async ctx =>
            {
                using (ctx.Scope)
                    await PipelineCases.Plain[kind](ctx.Scope.ServiceProvider);
            })
            .Then("The handler was disposed exactly once", ctx =>
                Assert.Equal(1, ctx.Probe.DisposalsOf(Assert.Single(ctx.Probe.HandlersConstructed))))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDisposableHandlerWithDecorator_WhenScopeEnds_ThenHandlerIsConstructedOnceAndDisposedExactlyOnce(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A disposable handler wrapped by a decorator", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope ends", async ctx =>
            {
                using (ctx.Scope)
                    await PipelineCases.Disp[kind](ctx.Scope.ServiceProvider);
            })
            .Then("One handler was constructed and it was disposed exactly once", ctx =>
                Assert.Equal(1, ctx.Probe.DisposalsOf(Assert.Single(ctx.Probe.HandlersConstructed))))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDisposableDecorator_WhenScopeEnds_ThenDecoratorIsDisposedExactlyOnce(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A handler wrapped by a disposable decorator that nobody registered by hand", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope ends", async ctx =>
            {
                using (ctx.Scope)
                    await PipelineCases.Disp[kind](ctx.Scope.ServiceProvider);
            })
            .Then("The decorator built for that dispatch was disposed exactly once", ctx =>
                Assert.Equal(1, ctx.Probe.DisposalsOf(Assert.Single(ctx.Probe.DecoratorsConstructed))))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratedAsyncOnlyDisposableHandler_WhenScopeDisposedSynchronously_ThenFailsLikeUndecoratedHandler(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A handler that only implements IAsyncDisposable, wrapped by a decorator", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope is disposed synchronously", async ctx =>
            {
                await PipelineCases.AsyncOnly[kind](ctx.Scope.ServiceProvider);
                ctx.Scope.Dispose();
            })
            .ThenThrows<InvalidOperationException>("Synchronous disposal fails, as it does for an undecorated async-only handler, instead of silently skipping it")
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratedAsyncDisposableHandler_WhenScopeDisposedAsynchronously_ThenDisposeAsyncIsCalledOnce(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A handler that only implements IAsyncDisposable, wrapped by a decorator", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope is disposed with await using", async ctx =>
            {
                await using ((IAsyncDisposable)ctx.Scope)
                    await PipelineCases.AsyncOnly[kind](ctx.Scope.ServiceProvider);
            })
            .Then("DisposeAsync ran exactly once", ctx =>
                Assert.Equal(1, ctx.Probe.DisposalsOf(Assert.Single(ctx.Probe.HandlersConstructed))))
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratorConstructorThrows_WhenDispatched_ThenCallerGetsOriginalExceptionAndHandlerIsDisposedOnce(string kind)
    {
        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A disposable handler whose decorator throws while being constructed", ctx => ctx.Build(AddSenderAndPublisherUnderTest))
            .When($"The {kind} handler is dispatched to inside a scope, and the scope ends", async ctx =>
            {
                using (ctx.Scope)
                    await PipelineCases.Faulty[kind](ctx.Scope.ServiceProvider);
            })
            .ThenThrows<DecoratorConstructionFailure>("The decorator constructor's exception reaches the caller")
                .True("It is the very instance the constructor threw", thrown => ReferenceEquals(thrown.Exception, thrown.State.Probe.DecoratorFailure))
            .And("By the time the scope ended, the already-built handler was disposed exactly once", thrown =>
                Assert.Equal(1, thrown.State.Probe.DisposalsOf(Assert.Single(thrown.State.Probe.HandlersConstructed))))
            .RunAsync();
    }
}
