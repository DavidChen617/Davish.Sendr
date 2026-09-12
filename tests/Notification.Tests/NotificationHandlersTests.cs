using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Notification.Tests;

public class NotificationHandlersTests
{
    [Fact]
    public async Task GivenIPublisher_WhenPublishNotification_ThenSequenceRunsInOrder()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x
                => x.Handler.Sequence
                    .With<FirstNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        await publisher.PublishAsync(new SomeNotification(), default);

        // Then
        Assert.Equal(["First", "Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenDecoratorOnOneEntry_WhenPublish_ThenOnlyThatEntryIsWrapped()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x
                => x.Handler.Sequence
                    .With<FirstNotificationHandler>(h => h.Decorator.With<LoggingNotificationDecorator>())
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        await publisher.PublishAsync(new SomeNotification(), default);

        // Then
        Assert.Equal(["Start", "First", "End", "Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenNoHandlersRegistered_WhenPublish_ThenNoOp()
    {
        // Given
        var provider = new ServiceCollection()
            .AddSendrNotification()
            .BuildServiceProvider();
        var publisher = provider.GetService<IPublisher>()!;

        // When / Then
        await publisher.PublishAsync(new SomeNotification(), default);
    }

    [Fact]
    public async Task GivenSyncThrowingSequenceHandler_WhenPublish_ThenLaterHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Sequence
                    .With<ThrowingNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: a handler that throws synchronously doesn't stop the rest of the Sequence either
        // — RunSequenceAsync's try/catch wraps the call itself, not just the await.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenSyncThrowingMiddleSequenceHandler_WhenPublish_ThenLaterHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Sequence
                    .With<FirstNotificationHandler>()
                    .With<ThrowingNotificationHandler>()
                    .With<ThirdNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["First", "Third"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenAsyncSequenceHandlerThrows_WhenPublish_ThenLaterHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Sequence
                    .With<FirstNotificationHandler>()
                    .With<ThrowingAsyncNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: Sequence keeps running in order after a failure — whether it throws
        // synchronously (above) or asynchronously (here). Since only one handler actually
        // failed, the single exception surfaces unwrapped.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("boom-async", exception.Message);
        Assert.Equal(["First", "Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenParallelHandlers_WhenPublish_ThenAllRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Parallel
                    .With<FirstNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        await publisher.PublishAsync(new SomeNotification(), default);

        // Then
        Assert.Equal(["First", "Second"], collector.LogCollection.OrderBy(x => x));
    }

    [Fact]
    public async Task GivenAsyncParallelHandlerThrows_WhenPublish_ThenOtherHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Parallel
                    .With<ThrowingAsyncNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Second", collector.LogCollection);
    }

    [Fact]
    public async Task GivenSyncThrowingParallelHandler_WhenPublish_ThenOtherHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Parallel
                    .With<ThrowingNotificationHandler>()
                    .With<SecondNotificationHandler>())
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: a handler that throws synchronously while starting doesn't stop the remaining
        // Parallel steps from starting either — RunParallelAsync's try/catch wraps each step's
        // invocation, not just its await.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Second", collector.LogCollection);
    }

    [Fact]
    public async Task GivenSequenceAndParallelHandlers_WhenPublish_ThenBothGroupsRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
            {
                x.Handler.Sequence.With<FirstNotificationHandler>();
                x.Handler.Parallel.With<SecondNotificationHandler>();
            })
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        await publisher.PublishAsync(new SomeNotification(), default);

        // Then
        Assert.Equal(["First", "Second"], collector.LogCollection.OrderBy(x => x));
    }

    [Fact]
    public async Task GivenSequenceThrows_WhenPublish_ThenParallelHandlersStillRun()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
            {
                x.Handler.Sequence.With<ThrowingNotificationHandler>();
                x.Handler.Parallel
                    .With<FirstNotificationHandler>()
                    .With<SecondNotificationHandler>();
            })
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: Sequence and Parallel start together — Sequence failing doesn't cancel Parallel.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["First", "Second"], collector.LogCollection.OrderBy(x => x));
    }

    [Fact]
    public async Task GivenParallelHandlerThrowsAsync_WhenPublish_ThenSequenceStillRunsToCompletion()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
            {
                x.Handler.Sequence
                    .With<FirstNotificationHandler>()
                    .With<SecondNotificationHandler>();
                x.Handler.Parallel.With<ThrowingAsyncNotificationHandler>();
            })
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: Parallel failing doesn't cancel Sequence — it still runs to completion, in order.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["First", "Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenBothGroupsThrow_WhenPublish_ThenAllHandlersRunAndBothExceptionsAggregate()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
            {
                x.Handler.Sequence
                    .With<FirstNotificationHandler>()
                    .With<ThrowingNotificationHandler>()
                    .With<ThirdNotificationHandler>();
                x.Handler.Parallel
                    .With<ThrowingAsyncNotificationHandler>()
                    .With<SecondNotificationHandler>();
            })
            .BuildServiceProvider();
        var collector = provider.GetService<LogCollector>()!;
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: neither group stops at its failure — "Third" still runs in Sequence, "Second"
        // still runs in Parallel. Both groups genuinely failed, so both exceptions surface
        // together rather than one silently losing to the other.
        var exception = await Assert.ThrowsAsync<AggregateException>(act);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, ex => Assert.IsType<InvalidOperationException>(ex));
        Assert.Contains(exception.InnerExceptions, ex => ex.Message == "boom");
        Assert.Contains(exception.InnerExceptions, ex => ex.Message == "boom-async");
        Assert.Equal(["First", "Third"], collector.LogCollection.Where(x => x is "First" or "Third"));
        Assert.Contains("Second", collector.LogCollection);
    }

    [Fact]
    public async Task GivenMultipleParallelHandlersThrow_WhenPublish_ThenAllExceptionsAreAggregated()
    {
        // Given
        var provider = new ServiceCollection()
            .AddScoped<LogCollector>()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x =>
                x.Handler.Parallel
                    .With<ThrowingAsyncNotificationHandler>()
                    .With<SecondThrowingAsyncNotificationHandler>())
            .BuildServiceProvider();
        var publisher = provider.GetService<IPublisher>()!;

        // When
        var act = () => publisher.PublishAsync(new SomeNotification(), default);

        // Then: a bare await Task.WhenAll(...) would only ever surface one of these — both
        // handlers actually failed, so both exceptions must be visible.
        var exception = await Assert.ThrowsAsync<AggregateException>(act);
        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, ex => ex.Message == "boom-async");
        Assert.Contains(exception.InnerExceptions, ex => ex.Message == "boom-async-2");
    }

    [Fact]
    public void GivenNotificationAlreadyRegistered_WhenAddNotificationHandlerAgain_ThenThrows()
    {
        // Given
        var services = new ServiceCollection()
            .AddSendrNotification()
            .AddNotificationHandler<SomeNotification>(x => x.Handler.Sequence
                .With<FirstNotificationHandler>());

        // When
        var act = () => services.AddNotificationHandler<SomeNotification>(x => x.Handler.Sequence
            .With<SecondNotificationHandler>());

        // Then
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public async Task GivenIPublisher_WhenPublishNullNotification_ThenThrowsArgumentNullException()
    {
        // Given
        var publisher = new ServiceCollection()
            .AddSendrNotification()
            .BuildServiceProvider()
            .GetRequiredService<IPublisher>();

        // When / Then
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => publisher.PublishAsync(null!, default));
        Assert.Equal("notification", exception.ParamName);
    }
}

public class LogCollector
{
    public readonly List<string> LogCollection = new();
}

public sealed record SomeNotification : INotification;

public sealed class FirstNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("First");
        return Task.CompletedTask;
    }
}

public sealed class SecondNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("Second");
        return Task.CompletedTask;
    }
}

public sealed class ThirdNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        collector.LogCollection.Add("Third");
        return Task.CompletedTask;
    }
}

public sealed class ThrowingNotificationHandler : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("boom");
}

public sealed class ThrowingAsyncNotificationHandler : INotificationHandler<SomeNotification>
{
    public async Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom-async");
    }
}

public sealed class SecondThrowingAsyncNotificationHandler : INotificationHandler<SomeNotification>
{
    public async Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom-async-2");
    }
}

public sealed class LoggingNotificationDecorator(LogCollector collector) : INotificationDecorator
{
    public async Task HandleAsync<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        collector.LogCollection.Add("Start");
        await next();
        collector.LogCollection.Add("End");
    }
}
