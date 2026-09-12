using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Message.Tests;

public class MessageTests
{
    [Fact]
    public async Task GivenISender_WhenSendCommand_ThenHandled()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddCommandHandler<SomeCommand, SomeCommandHandler>()
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeCommand(), default);

        // Then
        Assert.Equal(["Handled"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommandWithResponse_ThenHandlerResponseReturned()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddCommandHandler<SomeCommandWithResponse, SomeId, SomeCommandWithResponseHandler>()
            .BuildServiceProvider()
            .GetService<ISender>()!;

        // When
        var result = await sender.SendAsync(new SomeCommandWithResponse(), default);

        // Then
        Assert.Equal(new SomeId(1), result);
    }

    [Fact]
    public async Task GivenISender_WhenSendQuery_ThenHandlerResponseReturned()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddQueryHandler<SomeQuery, SomeDto, SomeQueryHandler>()
            .BuildServiceProvider()
            .GetService<ISender>()!;

        // When
        var result = await sender.SendAsync(new SomeQuery(), default);

        // Then
        Assert.IsType<SomeDto>(result);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommand_ThenHandledWithDecorator()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr()
            .AddCommandHandler<SomeCommand, SomeCommandHandler>(x =>
                x.Decorator.With<LoggingDecorator>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var sender = provider.GetService<ISender>()!;

        // When
        await sender.SendAsync(new SomeCommand(), default);

        // Then
        Assert.Equal(["Start", "Handled", "End"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenISender_WhenSendCommand_AndHandlerThrows_ThenExceptionPropagates()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddCommandHandler<ThrowingCommand, ThrowingCommandHandler>()
            .BuildServiceProvider()
            .GetService<ISender>()!;

        // When
        var act = () => sender.SendAsync(new ThrowingCommand(), default);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task GivenISender_WhenSendQuery_AndHandlerThrows_ThenExceptionPropagates()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddQueryHandler<ThrowingQuery, SomeDto, ThrowingQueryHandler>()
            .BuildServiceProvider()
            .GetService<ISender>()!;

        // When
        var act = () => sender.SendAsync(new ThrowingQuery(), default);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }
}

public sealed record SomeCommand : ICommand;

public sealed class SomeCommandHandler(LogCollector collector) : ICommandHandler<SomeCommand>
{
    public Task HandleAsync(SomeCommand command, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("Handled");
        return Task.CompletedTask;
    }
}

public sealed record SomeId(int Value);

public sealed record SomeCommandWithResponse : ICommand<SomeId>;

public sealed class SomeCommandWithResponseHandler : ICommandHandler<SomeCommandWithResponse, SomeId>
{
    public Task<SomeId> HandleAsync(SomeCommandWithResponse command, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SomeId(1));
    }
}

public sealed record SomeQuery : IQuery<SomeDto>;

public sealed record SomeDto;

public sealed class SomeQueryHandler : IQueryHandler<SomeQuery, SomeDto>
{
    public Task<SomeDto> HandleAsync(SomeQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SomeDto());
    }
}

public sealed record ThrowingCommand : ICommand;

public sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand>
{
    public Task HandleAsync(ThrowingCommand command, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom");
}

public sealed record ThrowingQuery : IQuery<SomeDto>;

public sealed class ThrowingQueryHandler : IQueryHandler<ThrowingQuery, SomeDto>
{
    public Task<SomeDto> HandleAsync(ThrowingQuery query, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom");
}

public sealed class LogCollector
{
    public readonly List<string> LogCollection = new();
}

public sealed class LoggingDecorator(LogCollector collector) : ICommandDecorator
{
    public async Task HandleAsync<TCommand>(
        TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        collector.LogCollection.Add("Start");
        await next();
        collector.LogCollection.Add("End");
    }
}
