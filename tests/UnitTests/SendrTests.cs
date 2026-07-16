using Davish.Sendr;
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
        var result = await sender.SendAsync(new SomeQuery());

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
        await sender.SendAsync(new SomeCommand());

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
        await sender.SendAsync(new SomeQuery());

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
        await sender.SendAsync(new SomeQuery());

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
        await sender.SendAsync(new SomeCommand());

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
        await sender.SendAsync(new SomeQuery());
        await sender.SendAsync(new SomeQuery());

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
        await sender.SendAsync(new SomeCommand());

        //Then
        Assert.Equal(
            ["BeginTransaction", "Start", "TaskVoidHandled", "End", "Commit"],
            collector.LogCollection);
    }
}

public sealed record SomeCommand : IRequest;

public sealed class SomeCommandHandler(LogCollector collector) : IRequestHandler<SomeCommand>
{
    public Task HandleAsync(SomeCommand request, CancellationToken cancellationToken = default)
    {
        collector.LogCollection.Add("TaskVoidHandled");
        return Task.CompletedTask;
    }
}

public record SomeQuery : IRequest<SomeDto>;

public record SomeDto;

public sealed class SomeQueryHandler : IRequestHandler<SomeQuery, SomeDto>
{
    public Task<SomeDto> HandleAsync(SomeQuery request, CancellationToken cancellationToken = default)
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
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        collector.LogCollection.Add("Start");
        var response = await next();
        collector.LogCollection.Add("End");
        return response;
    }

    public async Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken = default)
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
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        collector.LogCollection.Add("BeginTransaction");
        var response = await next();
        collector.LogCollection.Add("Commit");
        return response;
    }

    public async Task HandleAsync<TRequest>(
        TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        collector.LogCollection.Add("BeginTransaction");
        await next();
        collector.LogCollection.Add("Commit");
    }
}
