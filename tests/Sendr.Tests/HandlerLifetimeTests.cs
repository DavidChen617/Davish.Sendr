using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>handler-lifetime through the default reflection-based sender and publisher.</summary>
[Feature<HandlerLifetimeFeature>]
public class HandlerLifetimeTests : HandlerLifetimeSpec
{
    protected override IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services) =>
        services.AddSendr().AddSendrNotification().AddPipelineHandlersManually();

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindsButPublishData), MemberType = typeof(PipelineCases))]
    public async Task GivenDecoratorConstructorThrowsAndHandlerDisposeThrows_WhenDispatched_ThenBothExceptionsAreInAggregateException(string kind)
    {
        var disposeFailure = new HandlerDisposeFailure();

        await new Scenario()
            .Given<DecoratorsSpec.PipelineContext>("A handler whose Dispose throws, wrapped by a decorator whose constructor throws", ctx =>
                ctx.Build(services => services
                    .AddSingleton(disposeFailure)
                    .AddSendr()
                    .AddRequestHandler<BrokenRequest, BrokenRequestHandler>(x => x.Decorator.With<FaultyDecorator>())
                    .AddRequestHandler<BrokenRequestForValue, int, BrokenRequestForValueHandler>(x => x.Decorator.With<FaultyDecorator>())
                    .AddCommandHandler<BrokenCommand, BrokenCommandHandler>(x => x.Decorator.With<FaultyDecorator>())
                    .AddCommandHandler<BrokenCommandForValue, int, BrokenCommandForValueHandler>(x => x.Decorator.With<FaultyDecorator>())
                    .AddQueryHandler<BrokenQuery, int, BrokenQueryHandler>(x => x.Decorator.With<FaultyDecorator>())
                    .AddStreamRequestHandler<BrokenStream, int, BrokenStreamHandler>(x => x.Decorator.With<FaultyDecorator>())))
            .When($"The {kind} handler is dispatched to", ctx => Broken[kind](ctx.Scope.ServiceProvider))
            .ThenThrows<AggregateException>("Both failures reach the caller together")
                .True("The decorator constructor's exception is kept", thrown => thrown.Exception.InnerExceptions.Contains(thrown.State.Probe.DecoratorFailure))
                .True("The handler cleanup's exception is kept", thrown => thrown.Exception.InnerExceptions.Contains(disposeFailure))
            .RunAsync();
    }

    private static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Broken =
        PipelineCases.For<BrokenRequest, BrokenRequestForValue, BrokenCommand, BrokenCommandForValue, BrokenQuery, BrokenStream, BrokenNote>();
}

// Reflection-only: a decorator that fails to construct makes the sender dispose the handler it already built,
// and here that disposal fails too.

public sealed class HandlerDisposeFailure() : Exception("Raised by a handler's Dispose");

public sealed record BrokenRequest : IRequest;
public sealed record BrokenRequestForValue : IRequest<int>;
public sealed record BrokenCommand : ICommand;
public sealed record BrokenCommandForValue : ICommand<int>;
public sealed record BrokenQuery : IQuery<int>;
public sealed record BrokenStream : IStreamRequest<int>;
public sealed record BrokenNote : INotification;

public sealed class BrokenRequestHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineRequestHandler<BrokenRequest>(probe), IDisposable
{
    public void Dispose() => throw failure;
}

public sealed class BrokenRequestForValueHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineRequestForValueHandler<BrokenRequestForValue>(probe), IDisposable
{
    public void Dispose() => throw failure;
}

public sealed class BrokenCommandHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineCommandHandler<BrokenCommand>(probe), IDisposable
{
    public void Dispose() => throw failure;
}

public sealed class BrokenCommandForValueHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineCommandForValueHandler<BrokenCommandForValue>(probe), IDisposable
{
    public void Dispose() => throw failure;
}

public sealed class BrokenQueryHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineQueryHandler<BrokenQuery>(probe), IDisposable
{
    public void Dispose() => throw failure;
}

public sealed class BrokenStreamHandler(PipelineProbe probe, HandlerDisposeFailure failure) : PipelineStreamHandler<BrokenStream>(probe), IDisposable
{
    public void Dispose() => throw failure;
}
