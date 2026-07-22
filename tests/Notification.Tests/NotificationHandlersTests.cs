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
        await publisher.PublishAsync(new SomeNotification());

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
        await publisher.PublishAsync(new SomeNotification());

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
        await publisher.PublishAsync(new SomeNotification());
    }

    [Fact]
    public async Task GivenEarlierHandlerThrows_WhenPublish_ThenLaterHandlersDoNotRun()
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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(collector.LogCollection);
    }

    [Fact]
    public async Task GivenMiddleSequenceHandlerThrows_WhenPublish_ThenOnlyEarlierHandlerRan()
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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["First"], collector.LogCollection);
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
        await publisher.PublishAsync(new SomeNotification());

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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Second", collector.LogCollection);
    }

    // Pins a known gap (see NotificationHandlers<TNotification> remarks): a handler that throws
    // synchronously instead of via an async Task fault aborts the Select/Task.WhenAll enumeration,
    // so handlers queued after it never run — unlike the async-throw case above.
    [Fact]
    public async Task GivenSyncThrowingParallelHandler_WhenPublish_ThenLaterHandlersAreSkipped()
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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.DoesNotContain("Second", collector.LogCollection);
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
        await publisher.PublishAsync(new SomeNotification());

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
        var act = () => publisher.PublishAsync(new SomeNotification());

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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then: Parallel failing doesn't cancel Sequence — it still runs to completion, in order.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal(["First", "Second"], collector.LogCollection);
    }

    [Fact]
    public async Task GivenBothGroupsThrow_WhenPublish_ThenEarlierSequenceHandlerAndSurvivingParallelHandlerRan()
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
        var act = () => publisher.PublishAsync(new SomeNotification());

        // Then: Sequence fail-fast stops before "Third"; Parallel's surviving handler still runs.
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("First", collector.LogCollection);
        Assert.DoesNotContain("Third", collector.LogCollection);
        Assert.Contains("Second", collector.LogCollection);
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
}

public class LogCollector
{
    public readonly List<string> LogCollection = new();
}

public sealed record SomeNotification : INotification;

public sealed class FirstNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken = default)
    {
        collector.LogCollection.Add("First");
        return Task.CompletedTask;
    }
}

public sealed class SecondNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken = default)
    {
        collector.LogCollection.Add("Second");
        return Task.CompletedTask;
    }
}

public sealed class ThirdNotificationHandler(LogCollector collector) : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken = default)
    {
        collector.LogCollection.Add("Third");
        return Task.CompletedTask;
    }
}

public sealed class ThrowingNotificationHandler : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom");
}

public sealed class ThrowingAsyncNotificationHandler : INotificationHandler<SomeNotification>
{
    public async Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom-async");
    }
}

public sealed class LoggingNotificationDecorator(LogCollector collector) : INotificationDecorator
{
    public async Task HandleAsync<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        collector.LogCollection.Add("Start");
        await next();
        collector.LogCollection.Add("End");
    }
}
