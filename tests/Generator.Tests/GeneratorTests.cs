using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Generator.Tests;

public class GeneratorTests
{
    [Fact]
    public void GivenServiceProvider_WhenResolveISender_ThenGeneratedSenderIsUsed()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();

        // When
        var sender = provider.GetService<ISender>();

        // Then
        Assert.NotNull(sender);
        Assert.Equal("GeneratedSender", sender!.GetType().Name);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendQuery_ThenHandled()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        var result = await sender.SendAsync(new GenSomeQuery(), default);

        // Then
        Assert.IsType<GenSomeDto>(result);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendCommand_ThenHandled()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenSomeCommand(), default);

        // Then
        Assert.Equal(["TaskVoidHandled"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendDecoratedQuery_ThenDecoratorRunsAroundHandler()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenDecoratedQuery(), default);

        // Then
        Assert.Equal(["Start", "End"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendMultiDecoratedQuery_ThenFirstDeclaredDecoratorIsOutermost()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenMultiDecoratedQuery(), default);

        // Then
        Assert.Equal(["BeginTransaction", "Start", "End", "Commit"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendStreamRequest_ThenDecoratedStreamHandled()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var streamSender = provider.GetRequiredService<IStreamSender>();

        // When
        var items = new List<int>();
        await foreach (var item in streamSender.SendStream(new GenStreamQuery(), default))
            items.Add(item);

        // Then
        Assert.Equal([1, 2, 3], items);
        Assert.Equal(["StreamStart", "StreamEnd"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendCqrsCommand_ThenHandled()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenCqrsCommand(), default);

        // Then
        Assert.Equal(["CommandHandled"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendCqrsCommandWithResponse_ThenHandlerResponseReturned()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        var result = await sender.SendAsync(new GenCqrsCommandWithResponse(), default);

        // Then
        Assert.IsType<GenSomeDto>(result);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendCqrsQuery_ThenHandlerResponseReturned()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        var result = await sender.SendAsync(new GenCqrsQuery(), default);

        // Then
        Assert.IsType<GenSomeDto>(result);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendDecoratedCqrsCommand_ThenDecoratorRunsAroundHandler()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenDecoratedCqrsCommand(), default);

        // Then
        Assert.Equal(["CommandStart", "CommandHandled", "CommandEnd"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendDecoratedCqrsQuery_ThenDecoratorRunsAroundHandler()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var sender = provider.GetRequiredService<ISender>();

        // When
        await sender.SendAsync(new GenDecoratedCqrsQuery(), default);

        // Then
        Assert.Equal(["QueryStart", "QueryEnd"], collector.LogCollection);
    }

    [Fact]
    public void GivenServiceProvider_WhenResolveIPublisher_ThenGeneratedPublisherIsUsed()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendrNotification(o => o.UseGenerators())
            .BuildServiceProvider();

        // When
        var publisher = provider.GetService<IPublisher>();

        // Then
        Assert.NotNull(publisher);
        Assert.Equal("GeneratedPublisher", publisher!.GetType().Name);
    }

    [Fact]
    public async Task GivenNoHandlersRegistered_WhenPublishUnrelatedNotification_ThenNoOp()
    {
        // Given
        var publisher = new ServiceCollection()
            .AddSendrNotification(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<IPublisher>();

        // When / Then
        await publisher.PublishAsync(new GenUnhandledNotification(), default);
    }

    [Fact]
    public async Task GivenTwoHandlers_WhenPublishWithDefaultRunMode_ThenBothHandlersRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var publisher = provider.GetRequiredService<IPublisher>();

        // When
        await publisher.PublishAsync(new GenSomeNotification(), default);

        // Then
        Assert.Equal(2, collector.LogCollection.Count);
        Assert.Contains("FirstNotificationHandled", collector.LogCollection);
        Assert.Contains("SecondNotificationHandled", collector.LogCollection);
    }

    [Fact]
    public async Task GivenTwoHandlers_WhenPublishWithParallelRunMode_ThenBothHandlersRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel)))
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var publisher = provider.GetRequiredService<IPublisher>();

        // When
        await publisher.PublishAsync(new GenSomeNotification(), default);

        // Then
        Assert.Equal(2, collector.LogCollection.Count);
        Assert.Contains("FirstNotificationHandled", collector.LogCollection);
        Assert.Contains("SecondNotificationHandled", collector.LogCollection);
    }

    [Fact]
    public async Task GivenDecoratedNotificationHandler_WhenPublish_ThenDecoratorRunsAroundHandler()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification(o => o.UseGenerators())
            .BuildServiceProvider();
        var collector = provider.GetRequiredService<LogCollector>();
        var publisher = provider.GetRequiredService<IPublisher>();

        // When
        await publisher.PublishAsync(new GenDecoratedNotification(), default);

        // Then
        Assert.Equal(["NotificationStart", "NotificationHandled", "NotificationEnd"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendNullQuery_ThenThrowsArgumentNullException()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When / Then
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => sender.SendAsync((GenCqrsQuery)null!, default));
        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public async Task GivenGeneratedPublisher_WhenPublishNullNotification_ThenThrowsArgumentNullException()
    {
        // Given
        var publisher = new ServiceCollection()
            .AddSendrNotification(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<IPublisher>();

        // When / Then
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishAsync(null!, default));
        Assert.Equal("notification", exception.ParamName);
    }

    [Fact]
    public async Task GivenMultipleHandlersThrow_WhenPublish_ThenAllExceptionsAreAggregated()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel)))
            .BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();

        // When
        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => publisher.PublishAsync(new GenThrowingNotification(), default));

        // Then
        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task GivenRecordHandler_WhenSendQuery_ThenHandled()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        var result = await sender.SendAsync(new GenRecordHandledQuery(), default);

        // Then
        Assert.IsType<GenSomeDto>(result);
    }

    [Fact]
    public async Task GivenQueryTypeImplementsTwoResponses_WhenSendBoth_ThenEachGetsItsOwnHandler()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators())
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        var intResult = await sender.SendAsync<int>(new GenMultiResponseQuery(), default);
        var stringResult = await sender.SendAsync<string>(new GenMultiResponseQuery(), default);

        // Then
        Assert.Equal(1, intResult);
        Assert.Equal("hello", stringResult);
    }
}

public sealed record GenSomeCommand : IRequest;

public sealed class GenSomeCommandHandler(LogCollector collector) : IRequestHandler<GenSomeCommand>
{
    public Task HandleAsync(GenSomeCommand request, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("TaskVoidHandled");
        return Task.CompletedTask;
    }
}

public sealed record GenSomeQuery : IRequest<GenSomeDto>;

public sealed record GenSomeDto;

public sealed class GenSomeQueryHandler : IRequestHandler<GenSomeQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenSomeQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed record GenDecoratedQuery : IRequest<GenSomeDto>;

[Decorate<LoggingDecorator>]
public sealed class GenDecoratedQueryHandler : IRequestHandler<GenDecoratedQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenDecoratedQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed record GenMultiDecoratedQuery : IRequest<GenSomeDto>;

[Decorate<TransactionDecorator, LoggingDecorator>]
public sealed class GenMultiDecoratedQueryHandler : IRequestHandler<GenMultiDecoratedQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenMultiDecoratedQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed record GenStreamQuery : IStreamRequest<int>;

[Decorate<StreamLoggingDecorator>]
public sealed class GenStreamQueryHandler : IStreamRequestHandler<GenStreamQuery, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        GenStreamQuery request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
        yield return 3;
    }
}

public sealed class LogCollector
{
    public readonly List<string> LogCollection = [];
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

public sealed record GenCqrsCommand : ICommand;

public sealed class GenCqrsCommandHandler(LogCollector collector) : ICommandHandler<GenCqrsCommand>
{
    public Task HandleAsync(GenCqrsCommand command, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("CommandHandled");
        return Task.CompletedTask;
    }
}

public sealed record GenCqrsCommandWithResponse : ICommand<GenSomeDto>;

public sealed class GenCqrsCommandWithResponseHandler : ICommandHandler<GenCqrsCommandWithResponse, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenCqrsCommandWithResponse command, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed record GenCqrsQuery : IQuery<GenSomeDto>;

public sealed class GenCqrsQueryHandler : IQueryHandler<GenCqrsQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenCqrsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed record GenDecoratedCqrsCommand : ICommand;

[Decorate<CqrsLoggingDecorator>]
public sealed class GenDecoratedCqrsCommandHandler(LogCollector collector) : ICommandHandler<GenDecoratedCqrsCommand>
{
    public Task HandleAsync(GenDecoratedCqrsCommand command, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("CommandHandled");
        return Task.CompletedTask;
    }
}

public sealed record GenDecoratedCqrsQuery : IQuery<GenSomeDto>;

[Decorate<CqrsLoggingDecorator>]
public sealed class GenDecoratedCqrsQueryHandler : IQueryHandler<GenDecoratedCqrsQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenDecoratedCqrsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

public sealed class CqrsLoggingDecorator(LogCollector collector)
    : ICommandDecorator, IQueryDecorator
{
    public async Task HandleAsync<TCommand>(
        TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        collector.LogCollection.Add("CommandStart");
        await next();
        collector.LogCollection.Add("CommandEnd");
    }

    public async Task<TResponse> HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        where TQuery : IQuery<TResponse>
    {
        collector.LogCollection.Add("QueryStart");
        var response = await next();
        collector.LogCollection.Add("QueryEnd");
        return response;
    }
}

public sealed record GenUnhandledNotification : INotification;

public sealed record GenSomeNotification : INotification;

public sealed class FirstGenNotificationHandler(LogCollector collector) : INotificationHandler<GenSomeNotification>
{
    public Task HandleAsync(GenSomeNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("FirstNotificationHandled");
        return Task.CompletedTask;
    }
}

public sealed class SecondGenNotificationHandler(LogCollector collector) : INotificationHandler<GenSomeNotification>
{
    public Task HandleAsync(GenSomeNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("SecondNotificationHandled");
        return Task.CompletedTask;
    }
}

public sealed record GenDecoratedNotification : INotification;

[Decorate<NotificationLoggingDecorator>]
public sealed class GenDecoratedNotificationHandler(LogCollector collector) : INotificationHandler<GenDecoratedNotification>
{
    public Task HandleAsync(GenDecoratedNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("NotificationHandled");
        return Task.CompletedTask;
    }
}

public sealed class NotificationLoggingDecorator(LogCollector collector) : INotificationDecorator
{
    public async Task HandleAsync<TNotification>(
        TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
        where TNotification : INotification
    {
        collector.LogCollection.Add("NotificationStart");
        await next();
        collector.LogCollection.Add("NotificationEnd");
    }
}

public sealed record GenThrowingNotification : INotification;

public sealed class FirstThrowingGenNotificationHandler : INotificationHandler<GenThrowingNotification>
{
    public Task HandleAsync(GenThrowingNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("first");
}

public sealed class SecondThrowingGenNotificationHandler : INotificationHandler<GenThrowingNotification>
{
    public Task HandleAsync(GenThrowingNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("second");
}

public sealed record GenRecordHandledQuery : IQuery<GenSomeDto>;

// A record (not class) handler declaration: RecordDeclarationSyntax is a distinct Roslyn syntax
// node kind from ClassDeclarationSyntax, so this specifically exercises that the generator's
// syntax predicate discovers records too, not just classes.
public sealed record GenRecordHandledQueryHandler : IQueryHandler<GenRecordHandledQuery, GenSomeDto>
{
    public Task<GenSomeDto> HandleAsync(GenRecordHandledQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new GenSomeDto());
}

// IQuery<out TResponse> is covariant, so the same query type can legally implement it for more
// than one TResponse. This must not trip the SENDR002 ambiguous-handler diagnostic, since the two
// handlers below target different response types, not the same one.
public sealed record GenMultiResponseQuery : IQuery<int>, IQuery<string>;

public sealed class GenMultiResponseIntQueryHandler : IQueryHandler<GenMultiResponseQuery, int>
{
    public Task<int> HandleAsync(GenMultiResponseQuery query, CancellationToken cancellationToken)
        => Task.FromResult(1);
}

public sealed class GenMultiResponseStringQueryHandler : IQueryHandler<GenMultiResponseQuery, string>
{
    public Task<string> HandleAsync(GenMultiResponseQuery query, CancellationToken cancellationToken)
        => Task.FromResult("hello");
}
