using System.Diagnostics;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Notification.Tests;

// ActivityListener subscriptions are process-wide, so every assertion filters on notification and
// handler types that only this class publishes.
public class NotificationActivityTests : IDisposable
{
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;

    public NotificationActivityTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SendrActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_stopped)
                    _stopped.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private Activity[] PublishSpans(Type notificationType)
    {
        lock (_stopped)
            return _stopped
                .Where(a => (string?)a.GetTagItem("sendr.kind") == "notification"
                            && (string?)a.GetTagItem("sendr.request.type") == notificationType.FullName)
                .ToArray();
    }

    private Activity HandlerSpan(Type handlerType)
    {
        lock (_stopped)
            return Assert.Single(_stopped, a =>
                (string?)a.GetTagItem("sendr.kind") == "notification.handler"
                && (string?)a.GetTagItem("sendr.handler.type") == handlerType.FullName);
    }

    private static IPublisher CreatePublisher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection()
            .AddScoped<ParallelGate>()
            .AddScoped<DecoratorObservation>()
            .AddSendrNotification();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IPublisher>();
    }

    [Fact]
    public async Task GivenOneSequenceHandler_WhenPublish_ThenPublishSpanWithHandlerChildIsEmitted()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActNote>(x =>
            x.Handler.Sequence.With<ActNoteHandlerA>()));

        // When
        await publisher.PublishAsync(new ActNote(), default);

        // Then
        var publish = Assert.Single(PublishSpans(typeof(ActNote)));
        Assert.Equal("Publish ActNote", publish.DisplayName);
        Assert.Equal(ActivityKind.Internal, publish.Kind);
        Assert.True(publish.IsStopped);
        Assert.Equal(ActivityStatusCode.Unset, publish.Status);

        var handler = HandlerSpan(typeof(ActNoteHandlerA));
        Assert.Equal("Handle ActNoteHandlerA", handler.DisplayName);
        Assert.Equal("sequence", handler.GetTagItem("sendr.notification.group"));
        Assert.Equal(publish.SpanId, handler.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Unset, handler.Status);
    }

    [Fact]
    public async Task GivenTwoSequenceHandlers_WhenPublish_ThenBothAreSiblingsUnderPublishSpan()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActSeqNote>(x =>
            x.Handler.Sequence.With<ActSeqHandlerA>().With<ActSeqHandlerB>()));

        // When
        await publisher.PublishAsync(new ActSeqNote(), default);

        // Then
        var publish = Assert.Single(PublishSpans(typeof(ActSeqNote)));
        var first = HandlerSpan(typeof(ActSeqHandlerA));
        var second = HandlerSpan(typeof(ActSeqHandlerB));
        Assert.Equal(publish.SpanId, first.ParentSpanId);
        Assert.Equal(publish.SpanId, second.ParentSpanId);
        Assert.True(first.StartTimeUtc + first.Duration <= second.StartTimeUtc + TimeSpan.FromMilliseconds(1),
            "Sequence handlers should not overlap.");
    }

    [Fact]
    public async Task GivenTwoParallelHandlers_WhenPublish_ThenBothRunConcurrentlyUnderPublishSpan()
    {
        // Given — each handler only completes once BOTH have started, so this deadlocks (and the
        // WaitAsync below fails the test) unless the two actually run concurrently.
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActParNote>(x =>
            x.Handler.Parallel.With<ActParHandlerA>().With<ActParHandlerB>()));

        // When
        await publisher.PublishAsync(new ActParNote(), default).WaitAsync(TimeSpan.FromSeconds(5));

        // Then
        var publish = Assert.Single(PublishSpans(typeof(ActParNote)));
        var a = HandlerSpan(typeof(ActParHandlerA));
        var b = HandlerSpan(typeof(ActParHandlerB));
        Assert.Equal("parallel", a.GetTagItem("sendr.notification.group"));
        Assert.Equal("parallel", b.GetTagItem("sendr.notification.group"));
        Assert.Equal(publish.SpanId, a.ParentSpanId);
        Assert.Equal(publish.SpanId, b.ParentSpanId);
    }

    [Fact]
    public async Task GivenSequenceAndParallelGroups_WhenPublish_ThenEachHandlerCarriesItsOwnGroup()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActMixedNote>(x =>
        {
            x.Handler.Sequence.With<ActMixedSeqHandler>();
            x.Handler.Parallel.With<ActMixedParHandler>();
        }));

        // When
        await publisher.PublishAsync(new ActMixedNote(), default);

        // Then
        Assert.Equal("sequence", HandlerSpan(typeof(ActMixedSeqHandler)).GetTagItem("sendr.notification.group"));
        Assert.Equal("parallel", HandlerSpan(typeof(ActMixedParHandler)).GetTagItem("sendr.notification.group"));
    }

    [Fact]
    public async Task GivenDecoratedHandler_WhenPublish_ThenHandlerSpanIsCurrentInsideTheDecorator()
    {
        // Given
        var services = new ServiceCollection()
            .AddScoped<DecoratorObservation>()
            .AddSendrNotification()
            .AddNotificationHandler<ActDecoratedNote>(x =>
                x.Handler.Sequence.With<ActDecoratedHandler>(h => h.Decorator.With<ObservingDecorator>()))
            .BuildServiceProvider();
        var observation = services.GetRequiredService<DecoratorObservation>();

        // When
        await services.GetRequiredService<IPublisher>().PublishAsync(new ActDecoratedNote(), default);

        // Then
        Assert.Equal("Handle ActDecoratedHandler", observation.CurrentDisplayName);
    }

    [Fact]
    public async Task GivenOneHandlerFails_WhenPublish_ThenOnlyItsSpanAndThePublishSpanAreError()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActFailNote>(x =>
            x.Handler.Sequence.With<ActFailingHandler>().With<ActFailNoteOkHandler>()));

        // When
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new ActFailNote(), default));

        // Then
        Assert.Equal("act-boom", exception.Message);
        var publish = Assert.Single(PublishSpans(typeof(ActFailNote)));
        Assert.Equal(ActivityStatusCode.Error, publish.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, publish.GetTagItem("error.type"));

        var failed = HandlerSpan(typeof(ActFailingHandler));
        Assert.Equal(ActivityStatusCode.Error, failed.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.GetTagItem("error.type"));

        // The runner keeps going after a failure, and that handler's own span stays clean.
        var survivor = HandlerSpan(typeof(ActFailNoteOkHandler));
        Assert.Equal(ActivityStatusCode.Unset, survivor.Status);
    }

    [Fact]
    public async Task GivenSeveralHandlersFail_WhenPublish_ThenPublishSpanRecordsAggregateException()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActMultiFailNote>(x =>
            x.Handler.Sequence.With<ActMultiFailHandlerA>().With<ActMultiFailHandlerB>()));

        // When
        await Assert.ThrowsAsync<AggregateException>(() => publisher.PublishAsync(new ActMultiFailNote(), default));

        // Then
        var publish = Assert.Single(PublishSpans(typeof(ActMultiFailNote)));
        Assert.Equal(ActivityStatusCode.Error, publish.Status);
        Assert.Equal(typeof(AggregateException).FullName, publish.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, HandlerSpan(typeof(ActMultiFailHandlerA)).Status);
        Assert.Equal(ActivityStatusCode.Error, HandlerSpan(typeof(ActMultiFailHandlerB)).Status);
    }

    [Fact]
    public async Task GivenHandlerIsCanceled_WhenPublish_ThenSpansAreMarkedError()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActCancelNote>(x =>
            x.Handler.Sequence.With<ActCancelHandler>()));

        // When
        await Assert.ThrowsAsync<OperationCanceledException>(() => publisher.PublishAsync(new ActCancelNote(), default));

        // Then
        var handler = HandlerSpan(typeof(ActCancelHandler));
        Assert.Equal(ActivityStatusCode.Error, handler.Status);
        Assert.Equal(typeof(OperationCanceledException).FullName, handler.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, Assert.Single(PublishSpans(typeof(ActCancelNote))).Status);
    }

    [Fact]
    public async Task GivenNoHandlersRegistered_WhenPublish_ThenNoSpanIsEmitted()
    {
        // Given
        var publisher = CreatePublisher(_ => { });

        // When
        await publisher.PublishAsync(new ActUnhandledNote(), default);

        // Then
        Assert.Empty(PublishSpans(typeof(ActUnhandledNote)));
    }

    [Fact]
    public async Task GivenListener_WhenPublishReturnsToCaller_ThenActivityCurrentIsNotLeaked()
    {
        // Given
        var publisher = CreatePublisher(s => s.AddNotificationHandler<ActNote>(x =>
            x.Handler.Sequence.With<ActNoteHandlerA>()));
        var before = Activity.Current;

        // When
        var pending = publisher.PublishAsync(new ActNote(), default);
        var duringCall = Activity.Current;
        await pending;

        // Then
        Assert.Same(before, duringCall);
        Assert.Same(before, Activity.Current);
    }
}

public sealed class ParallelGate
{
    private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public Task ArriveAsync()
    {
        if (Interlocked.Increment(ref _arrived) == 2)
            _both.SetResult();

        return _both.Task;
    }
}

public sealed class DecoratorObservation
{
    public string? CurrentDisplayName { get; set; }
}

public sealed class ObservingDecorator(DecoratorObservation observation) : INotificationDecorator
{
    public async Task HandleAsync<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        observation.CurrentDisplayName = Activity.Current?.DisplayName;
        await next();
    }
}

public sealed record ActNote : INotification;

public sealed class ActNoteHandlerA : INotificationHandler<ActNote>
{
    public Task HandleAsync(ActNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActSeqNote : INotification;

public sealed class ActSeqHandlerA : INotificationHandler<ActSeqNote>
{
    public async Task HandleAsync(ActSeqNote notification, CancellationToken cancellationToken)
        => await Task.Delay(5, cancellationToken);
}

public sealed class ActSeqHandlerB : INotificationHandler<ActSeqNote>
{
    public Task HandleAsync(ActSeqNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActParNote : INotification;

public sealed class ActParHandlerA(ParallelGate gate) : INotificationHandler<ActParNote>
{
    public Task HandleAsync(ActParNote notification, CancellationToken cancellationToken) => gate.ArriveAsync();
}

public sealed class ActParHandlerB(ParallelGate gate) : INotificationHandler<ActParNote>
{
    public Task HandleAsync(ActParNote notification, CancellationToken cancellationToken) => gate.ArriveAsync();
}

public sealed record ActMixedNote : INotification;

public sealed class ActMixedSeqHandler : INotificationHandler<ActMixedNote>
{
    public Task HandleAsync(ActMixedNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ActMixedParHandler : INotificationHandler<ActMixedNote>
{
    public Task HandleAsync(ActMixedNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActDecoratedNote : INotification;

public sealed class ActDecoratedHandler : INotificationHandler<ActDecoratedNote>
{
    public Task HandleAsync(ActDecoratedNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActFailNote : INotification;

public sealed class ActFailingHandler : INotificationHandler<ActFailNote>
{
    public Task HandleAsync(ActFailNote notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("act-boom");
}

public sealed class ActFailNoteOkHandler : INotificationHandler<ActFailNote>
{
    public Task HandleAsync(ActFailNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActMultiFailNote : INotification;

public sealed class ActMultiFailHandlerA : INotificationHandler<ActMultiFailNote>
{
    public Task HandleAsync(ActMultiFailNote notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("a");
}

public sealed class ActMultiFailHandlerB : INotificationHandler<ActMultiFailNote>
{
    public Task HandleAsync(ActMultiFailNote notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("b");
}

public sealed record ActCancelNote : INotification;

public sealed class ActCancelHandler : INotificationHandler<ActCancelNote>
{
    public Task HandleAsync(ActCancelNote notification, CancellationToken cancellationToken)
        => throw new OperationCanceledException();
}

public sealed record ActUnhandledNote : INotification;
