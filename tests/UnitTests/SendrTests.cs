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
