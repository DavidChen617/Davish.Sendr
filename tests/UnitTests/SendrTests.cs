using Davish.Sendr;
using Davish.Sendr.Implements;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests;

public class SendrTests
{
    [Fact]
    public void GivenServiceProvider_WhenResolveISender_ThenNotNull()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr()
            .BuildServiceProvider();

        // When
        var sender = provider.GetService<ISender>();

        // Then
        Assert.NotNull(sender);
    }

    [Fact]
    public void GivenServiceProvider_WhenResolveISender_ThenTypeofSender()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr()
            .BuildServiceProvider();

        // When
        var sender = provider.GetService<ISender>();

        // Then
        Assert.IsType<Sender>(sender);
    }

    [Fact]
    public async Task GivenISender_WhenResolveQueryHandler_ThenHandleWithQueryResultType()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SomeQuery, SomeDto, SomeQueryHandler>()
            .BuildServiceProvider()
            .GetService<ISender>()!;

        // When
        var result = await sender.SendAsync(new SomeQuery(), default);

        // Then
        Assert.IsType<SomeDto>(result);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommandRequest_ThenTaskVoidHandled()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr()
            .AddScoped<LogCollector>()
            .AddRequestHandler<SomeCommand, SomeCommandHandler>()
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeCommand(), default);

        // Then
        Assert.Equal(["TaskVoidHandled"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendQueryRequest_ThenHandleWithDecorator()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeQuery, SomeDto, SomeQueryHandler>(x =>
                x.Decorator.With<LoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeQuery(), default);

        //Then
        Assert.Equal(["Start", "End"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendRequestHandler_ThenHandleWithMultipleDecorator()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeQuery, SomeDto, SomeQueryHandler>(x => x.Decorator
                .With<TransactionDecorator>()
                .With<LoggingDecorator>())
            .BuildServiceProvider();

        var collector = provider.GetService<LogCollector>()!;

        // When
        var sender = provider.GetService<ISender>()!;
        await sender.SendAsync(new SomeQuery(), default);

        //Then
        Assert.Equal(["BeginTransaction", "Start", "End", "Commit"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommandRequest_ThenHandleWithDecorator()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeCommand, SomeCommandHandler>(x =>
                x.Decorator.With<LoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeCommand(), default);

        //Then
        Assert.Equal(["Start", "TaskVoidHandled", "End"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenSameProvider_WhenSendTwice_ThenDecoratorOrderStable()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeQuery, SomeDto, SomeQueryHandler>(x => x.Decorator
                .With<TransactionDecorator>()
                .With<LoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeQuery(), default);
        await sender.SendAsync(new SomeQuery(), default);

        // Then
        Assert.Equal(
            [
                "BeginTransaction", "Start", "End", "Commit",
                "BeginTransaction", "Start", "End", "Commit"
            ],
            collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommandHandler_ThenHandleWithMultipleDecorator()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeCommand, SomeCommandHandler>(x =>
                x.Decorator
                    .With<TransactionDecorator>()
                    .With<LoggingDecorator>())
            .BuildServiceProvider();

        var collector = provider.GetService<LogCollector>()!;

        // When
        var sender = provider.GetService<ISender>()!;
        await sender.SendAsync(new SomeCommand(), default);

        //Then
        Assert.Equal(
            ["BeginTransaction", "Start", "TaskVoidHandled", "End", "Commit"],
            collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendNullRequest_ThenThrowsArgumentNullException()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When / Then
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sender.SendAsync((IRequest)null!, default));
        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task GivenISender_WhenSendNullQuery_ThenThrowsArgumentNullException()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When / Then
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sender.SendAsync((IQuery<int>)null!, default));
        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public void GivenRequestHandlerAlreadyRegistered_WhenAddRequestHandlerAgain_ThenThrowsInvalidOperationException()
    {
        // Given
        var services = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SomeCommand, SomeCommandHandler>();

        // When / Then
        Assert.Throws<InvalidOperationException>(
            () => services.AddRequestHandler<SomeCommand, SecondSomeCommandHandler>());
    }

    [Fact]
    public async Task GivenQueryTypeImplementsTwoResponses_WhenSendBothConcurrently_ThenEachGetsItsOwnHandler()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddQueryHandler<MultiResponseQuery, int, IntQueryHandler>()
            .AddQueryHandler<MultiResponseQuery, string, StringQueryHandler>()
            .BuildServiceProvider()
            .GetRequiredService<ISender>();
        var query = new MultiResponseQuery();

        // When
        var tasks = new List<Task>();
        for (var i = 0; i < 200; i++)
        {
            var wantInt = i % 2 == 0;
            tasks.Add(wantInt
                ? sender.SendAsync<int>(query, default)
                : sender.SendAsync<string>(query, default));
        }
        await Task.WhenAll(tasks);

        // Then
        var intResult = await sender.SendAsync<int>(query, default);
        var stringResult = await sender.SendAsync<string>(query, default);
        Assert.Equal(1, intResult);
        Assert.Equal("hello", stringResult);
    }

    [Fact]
    public void GivenStreamRequest_WhenSendStreamNotYetEnumerated_ThenHandlerAndDecoratorNotResolved()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddStreamRequestHandler<SomeStreamQuery, int, SomeStreamQueryHandler>(x =>
                x.Decorator.With<StreamLoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var streamSender = provider.GetService<IStreamSender>()!;

        // When
        _ = streamSender.SendStream(new SomeStreamQuery(), default);

        // Then
        Assert.Empty(collector.LogCollection);
    }

    [Fact]
    public async Task GivenStreamRequest_WhenEnumerated_ThenHandlerAndDecoratorRunInOrder()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddStreamRequestHandler<SomeStreamQuery, int, SomeStreamQueryHandler>(x =>
                x.Decorator.With<StreamLoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var streamSender = provider.GetService<IStreamSender>()!;

        // When
        var items = new List<int>();
        await foreach (var item in streamSender.SendStream(new SomeStreamQuery(), default))
            items.Add(item);

        // Then
        Assert.Equal([1, 2], items);
        Assert.Equal(["StreamStart", "StreamEnd"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenDisposableTransientHandler_WhenScopeEnds_ThenDisposedExactlyOnce()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SomeCommand, DisposableCommandHandler>()
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.SendAsync(new SomeCommand(), default);
            Assert.Equal(1, DisposableCommandHandler.ConstructedCount);
        }

        // Then
        Assert.Equal(1, DisposableCommandHandler.DisposedCount);
    }

    [Fact]
    public async Task GivenDisposableHandlerWithDecorator_WhenScopeEnds_ThenDisposedExactlyOnce()
    {
        // Given
        var probe = new DisposableProbe();
        var provider = new ServiceCollection()
            .AddSingleton(probe)
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeCommand, ProbedDisposableCommandHandler>(x =>
                x.Decorator.With<LoggingDecorator>())
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.SendAsync(new SomeCommand(), default);
            Assert.Equal(1, probe.ConstructedCount);
        }

        // Then
        Assert.Equal(1, probe.DisposedCount);
    }

    [Fact]
    public async Task GivenDecoratorConstructorThrows_WhenSendAsync_ThenAlreadyConstructedHandlerIsDisposed()
    {
        // Given
        var probe = new DisposableProbe();
        var provider = new ServiceCollection()
            .AddSingleton(probe)
            .AddSendr()
            .AddRequestHandler<SomeCommand, ProbedDisposableCommandHandler>(x =>
                x.Decorator.With<ThrowingConstructorDecorator>())
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sender.SendAsync(new SomeCommand(), default));
        }

        // Then
        Assert.Equal(1, probe.ConstructedCount);
        Assert.Equal(1, probe.DisposedCount);
    }

    [Fact]
    public async Task GivenDecoratorConstructorThrowsAndHandlerCleanupAlsoThrows_WhenSendAsync_ThenBothFailuresRemainObservable()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<SomeCommand, ThrowsOnDisposeCommandHandler>(x =>
                x.Decorator.With<ThrowingConstructorDecorator>())
            .BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // When
        var thrown = await Record.ExceptionAsync(() => sender.SendAsync(new SomeCommand(), default));

        // Then
        // No single wrapping shape is mandated (AggregateException, InnerException chain, or
        // otherwise) — only that neither failure silently disappears. Before this fix, the
        // decorator construction failure (why cleanup was even attempted) was lost entirely
        // whenever DisposeOnFailure's own cleanup attempt also threw, leaving only the secondary
        // cleanup exception for the caller to see.
        var allExceptions = FlattenExceptions(thrown!).ToList();
        Assert.Contains(allExceptions, e => e is InvalidOperationException { Message: "decorator ctor failed" });
        Assert.Contains(allExceptions, e => e is ApplicationException { Message: "secondary cleanup failure" });
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var flattened in FlattenExceptions(inner))
                    yield return flattened;
        }
        else if (exception.InnerException is not null)
        {
            foreach (var flattened in FlattenExceptions(exception.InnerException))
                yield return flattened;
        }
    }

    [Fact]
    public async Task GivenDisposableQueryHandlerWithDecorator_WhenScopeEnds_ThenDisposedExactlyOnce()
    {
        // Given
        var probe = new DisposableProbe();
        var provider = new ServiceCollection()
            .AddSingleton(probe)
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddQueryHandler<SomeDisposableQuery, SomeDto, ProbedDisposableQueryHandler>(x =>
                x.Decorator.With<QueryLoggingDecorator>())
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.SendAsync(new SomeDisposableQuery(), default);
            Assert.Equal(1, probe.ConstructedCount);
        }

        // Then
        Assert.Equal(1, probe.DisposedCount);
    }

    [Fact]
    public void GivenReentrantRequestHandlerRegistration_WhenConfigureRegistersSameRequestAgain_ThenThrows()
    {
        // Given
        var services = new ServiceCollection().AddSendr();

        // When / Then
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRequestHandler<SomeCommand, SomeCommandHandler>(_ =>
                services.AddRequestHandler<SomeCommand, SecondSomeCommandHandler>()));
    }

    [Fact]
    public async Task GivenDecoratedAsyncOnlyHandler_WhenSyncScopeDisposed_ThenThrowsInvalidOperationException()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddRequestHandler<SomeCommand, AsyncOnlyDisposableCommandHandler>(x =>
                x.Decorator.With<LoggingDecorator>())
            .BuildServiceProvider();

        // When
        var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.SendAsync(new SomeCommand(), default);

        // Then
        // Matches how the container itself handles an undecorated handler that only implements
        // IAsyncDisposable: a synchronous scope can't finish its cleanup, so it must say so
        // instead of silently skipping it.
        Assert.Throws<InvalidOperationException>(() => scope.Dispose());
    }

    [Fact]
    public async Task GivenRetainedRequestHandlerOptions_WhenMutatedAfterBuildServiceProvider_ThenBuiltProviderPipelineUnaffected()
    {
        // Given
        var counter = new CallCounter();
        RequestHandlerOptions<SomeCommand>? retainedOptions = null;
        var provider = new ServiceCollection()
            .AddSingleton(counter)
            .AddScoped<LogCollector>()
            .AddTransient<CountingDecorator>()
            .AddSendr()
            .AddRequestHandler<SomeCommand, SomeCommandHandler>(x => retainedOptions = x)
            .BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        await sender.SendAsync(new SomeCommand(), default);

        // When
        // CountingDecorator is pre-registered in DI above so that, if this snapshot fix regresses,
        // the call below would actually succeed and run it — rather than the test passing only
        // because resolving an unregistered decorator type throws for an unrelated reason.
        retainedOptions!.Decorator.With<CountingDecorator>();
        await sender.SendAsync(new SomeCommand(), default);

        // Then
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task GivenDisposableCustomSender_WhenScopeResolvesOnlyISender_ThenDisposedExactlyOnce()
    {
        // Given
        var probe = new DisposableProbe();
        var provider = new ServiceCollection()
            .AddSingleton(probe)
            .AddSendr(o => o.UseSender<DisposableCustomSender>())
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<ISender>();
            Assert.Equal(1, probe.ConstructedCount);
        }

        // Then
        Assert.Equal(1, probe.DisposedCount);
    }

    [Fact]
    public async Task GivenDisposableCustomSender_WhenScopeResolvesBothISenderAndIStreamSender_ThenDisposedTwice()
    {
        // Given
        // Documents a residual, not a full fix: ISender and IStreamSender are two distinct DI
        // registrations that both resolve to the same TSender instance within a scope (verified
        // by ConstructedCount staying at 1). The container captures a disposable for disposal
        // once per registration whose factory returns it, regardless of shared identity, so
        // resolving both interfaces still means two Dispose calls on the one instance — down
        // from three before this fix (which also captured a separate, redundant TSender
        // registration). A custom ISender/IStreamSender implementation should make its disposal
        // idempotent if it's disposable, as documented on SendrOptions.UseSender.
        var probe = new DisposableProbe();
        var provider = new ServiceCollection()
            .AddSingleton(probe)
            .AddSendr(o => o.UseSender<DisposableCustomSender>())
            .BuildServiceProvider();

        // When
        await using (var scope = provider.CreateAsyncScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<ISender>();
            _ = scope.ServiceProvider.GetRequiredService<IStreamSender>();
            Assert.Equal(1, probe.ConstructedCount);
        }

        // Then
        Assert.Equal(2, probe.DisposedCount);
    }
}

public sealed class DisposableCustomSender : ISender, IDisposable
{
    private readonly DisposableProbe _probe;

    public DisposableCustomSender(DisposableProbe probe)
    {
        _probe = probe;
        _probe.ConstructedCount++;
    }

    public Task SendAsync(IRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
        => Task.FromResult<TResponse>(default!);

    public Task SendAsync(ICommand command, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
        => Task.FromResult<TResponse>(default!);

    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken)
        => Task.FromResult<TResponse>(default!);

    public IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public void Dispose() => _probe.DisposedCount++;
}

public sealed class DisposableCommandHandler : IRequestHandler<SomeCommand>, IDisposable
{
    public static int ConstructedCount;
    public static int DisposedCount;

    public DisposableCommandHandler() => ConstructedCount++;

    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => DisposedCount++;
}

public sealed class AsyncOnlyDisposableCommandHandler : IRequestHandler<SomeCommand>, IAsyncDisposable
{
    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ThrowsOnDisposeCommandHandler : IRequestHandler<SomeCommand>, IDisposable
{
    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => throw new ApplicationException("secondary cleanup failure");
}

public sealed class CallCounter
{
    public int Count;
}

public sealed class CountingDecorator(CallCounter counter) : IRequestDecorator
{
    public Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        counter.Count++;
        return next();
    }
}

public sealed class DisposableProbe
{
    public int ConstructedCount;
    public int DisposedCount;
}

public sealed class ProbedDisposableCommandHandler : IRequestHandler<SomeCommand>, IDisposable
{
    private readonly DisposableProbe _probe;

    public ProbedDisposableCommandHandler(DisposableProbe probe)
    {
        _probe = probe;
        _probe.ConstructedCount++;
    }

    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _probe.DisposedCount++;
}

public sealed class ThrowingConstructorDecorator : IRequestDecorator
{
    public ThrowingConstructorDecorator() => throw new InvalidOperationException("decorator ctor failed");

    public Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
        => next();
}

public sealed class SecondSomeCommandHandler : IRequestHandler<SomeCommand>
{
    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record MultiResponseQuery : IQuery<int>, IQuery<string>;

public sealed class IntQueryHandler : IQueryHandler<MultiResponseQuery, int>
{
    public Task<int> HandleAsync(MultiResponseQuery query, CancellationToken cancellationToken)
        => Task.FromResult(1);
}

public sealed class StringQueryHandler : IQueryHandler<MultiResponseQuery, string>
{
    public Task<string> HandleAsync(MultiResponseQuery query, CancellationToken cancellationToken)
        => Task.FromResult("hello");
}

public sealed record SomeCommand : IRequest;

public sealed class SomeCommandHandler(LogCollector collector) : IRequestHandler<SomeCommand>
{
    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("TaskVoidHandled");
        return Task.CompletedTask;
    }
}

public record SomeQuery : IRequest<SomeDto>;

public record SomeDto;

public sealed class SomeQueryHandler : IRequestHandler<SomeQuery, SomeDto>
{
    public Task<SomeDto> HandleAsync(SomeQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SomeDto());
    }
}

public class LogCollector
{
    public readonly List<string> LogCollection = new();
}

public sealed class LoggingDecorator(LogCollector collector)
    : IRequestDecorator, IRequestDecorator.WithResponse
{
    public async Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        collector.LogCollection.Add("Start");
        var response = await next();
        collector.LogCollection.Add("End");
        return response;
    }

    public async Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        collector.LogCollection.Add("Start");
        await next();
        collector.LogCollection.Add("End");
    }
}

public sealed class TransactionDecorator(LogCollector collector)
    : IRequestDecorator, IRequestDecorator.WithResponse
{
    public async Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        collector.LogCollection.Add("BeginTransaction");
        var response = await next();
        collector.LogCollection.Add("Commit");
        return response;
    }

    public async Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        collector.LogCollection.Add("BeginTransaction");
        await next();
        collector.LogCollection.Add("Commit");
    }
}

public sealed record SomeStreamQuery : IStreamRequest<int>;

public sealed class SomeStreamQueryHandler : IStreamRequestHandler<SomeStreamQuery, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        SomeStreamQuery request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
    }
}

public sealed class StreamLoggingDecorator(LogCollector collector) : IStreamRequestDecorator
{
    public async IAsyncEnumerable<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
    {
        collector.LogCollection.Add("StreamStart");
        await foreach (var item in next().WithCancellation(cancellationToken))
            yield return item;
        collector.LogCollection.Add("StreamEnd");
    }
}

public sealed record SomeDisposableQuery : IQuery<SomeDto>;

public sealed class ProbedDisposableQueryHandler : IQueryHandler<SomeDisposableQuery, SomeDto>, IDisposable
{
    private readonly DisposableProbe _probe;

    public ProbedDisposableQueryHandler(DisposableProbe probe)
    {
        _probe = probe;
        _probe.ConstructedCount++;
    }

    public Task<SomeDto> HandleAsync(SomeDisposableQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new SomeDto());

    public void Dispose() => _probe.DisposedCount++;
}

public sealed class QueryLoggingDecorator(LogCollector collector) : IQueryDecorator
{
    public async Task<TResponse> HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        where TQuery : IQuery<TResponse>
    {
        collector.LogCollection.Add("Start");
        var response = await next();
        collector.LogCollection.Add("End");
        return response;
    }
}
