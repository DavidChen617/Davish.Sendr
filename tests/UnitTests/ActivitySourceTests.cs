using System.Diagnostics;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests;

// ActivityListener subscriptions are process-wide, so other test classes dispatching through
// Sender at the same time also reach this listener. Every assertion filters on a request type
// that only this class sends.
public class ActivitySourceTests : IDisposable
{
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;

    public ActivitySourceTests()
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

    private Activity[] ActivitiesFor(Type requestType)
    {
        lock (_stopped)
            return _stopped.Where(a => (string?)a.GetTagItem("sendr.request.type") == requestType.FullName).ToArray();
    }

    private static ISender CreateSender() => new ServiceCollection()
        .AddSendr()
        .AddRequestHandler<TracedRequest, TracedRequestHandler>()
        .AddRequestHandler<TracedQuery, string, TracedQueryHandler>()
        .AddCommandHandler<TracedCommand, TracedCommandHandler>()
        .AddCommandHandler<TracedCommandWithResponse, int, TracedCommandWithResponseHandler>()
        .AddQueryHandler<TracedQ, string, TracedQHandler>()
        .AddRequestHandler<GatedRequest, GatedRequestHandler>()
        .AddRequestHandler<FailingRequest, FailingRequestHandler>()
        .AddRequestHandler<ThrowsSynchronouslyRequest, ThrowsSynchronouslyHandler>()
        .BuildServiceProvider()
        .GetRequiredService<ISender>();

    [Fact]
    public async Task GivenListener_WhenSendRequest_ThenActivityIsEmittedWithKindAndTypeTags()
    {
        // Given
        var sender = CreateSender();

        // When
        await sender.SendAsync(new TracedRequest(), default);

        // Then
        var activity = Assert.Single(ActivitiesFor(typeof(TracedRequest)));
        Assert.Equal("Send TracedRequest", activity.DisplayName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("request", activity.GetTagItem("sendr.kind"));
        Assert.True(activity.IsStopped);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    public async Task GivenListener_WhenSendRequestWithResponse_ThenResultIsReturnedAndActivityEmitted()
    {
        // Given
        var sender = CreateSender();

        // When
        var result = await sender.SendAsync(new TracedQuery(), default);

        // Then
        Assert.Equal("query-result", result);
        var activity = Assert.Single(ActivitiesFor(typeof(TracedQuery)));
        Assert.Equal("request", activity.GetTagItem("sendr.kind"));
    }

    [Fact]
    public async Task GivenListener_WhenSendCommand_ThenKindIsCommand()
    {
        // Given
        var sender = CreateSender();

        // When
        await sender.SendAsync(new TracedCommand(), default);
        var result = await sender.SendAsync(new TracedCommandWithResponse(), default);

        // Then
        Assert.Equal(42, result);
        Assert.Equal("command", Assert.Single(ActivitiesFor(typeof(TracedCommand))).GetTagItem("sendr.kind"));
        Assert.Equal("command", Assert.Single(ActivitiesFor(typeof(TracedCommandWithResponse))).GetTagItem("sendr.kind"));
    }

    [Fact]
    public async Task GivenListener_WhenSendQuery_ThenKindIsQuery()
    {
        // Given
        var sender = CreateSender();

        // When
        var result = await sender.SendAsync(new TracedQ(), default);

        // Then
        Assert.Equal("q-result", result);
        Assert.Equal("query", Assert.Single(ActivitiesFor(typeof(TracedQ))).GetTagItem("sendr.kind"));
    }

    [Fact]
    public async Task GivenHandlerThrows_WhenSend_ThenExceptionPropagatesAndActivityIsMarkedError()
    {
        // Given
        var sender = CreateSender();

        // When
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(new FailingRequest(), default));

        // Then
        Assert.Equal("boom", exception.Message);
        var activity = Assert.Single(ActivitiesFor(typeof(FailingRequest)));
        Assert.True(activity.IsStopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task GivenHandlerThrowsSynchronously_WhenSend_ThenActivityIsStillStopped()
    {
        // Given
        var sender = CreateSender();

        // When
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(new ThrowsSynchronouslyRequest(), default));

        // Then
        var activity = Assert.Single(ActivitiesFor(typeof(ThrowsSynchronouslyRequest)));
        Assert.True(activity.IsStopped);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task GivenHandlerIsCanceled_WhenSend_ThenActivityIsMarkedError()
    {
        // Given
        var sender = new ServiceCollection()
            .AddSendr()
            .AddRequestHandler<CanceledRequest, CanceledRequestHandler>()
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        // When
        await Assert.ThrowsAsync<OperationCanceledException>(() => sender.SendAsync(new CanceledRequest(), default));

        // Then
        var activity = Assert.Single(ActivitiesFor(typeof(CanceledRequest)));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(OperationCanceledException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task GivenNoListenerSampling_WhenSend_ThenNothingIsEmittedAndResultIsReturned()
    {
        // Given
        _listener.Dispose();
        using var samplesNothing = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SendrActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(samplesNothing);
        var sender = CreateSender();

        // When
        var result = await sender.SendAsync(new TracedQuery(), default);

        // Then
        Assert.Equal("query-result", result);
        Assert.Empty(ActivitiesFor(typeof(TracedQuery)));
    }

    [Fact]
    public async Task GivenListener_WhenSendReturnsToCaller_ThenActivityCurrentIsNotLeaked()
    {
        // Given
        var sender = CreateSender();
        var before = Activity.Current;
        var gate = new TaskCompletionSource();
        var pending = sender.SendAsync(new GatedRequest(gate.Task), default);

        // When — checked after SendAsync handed back a still-running Task, before it completes
        var duringCall = Activity.Current;
        gate.SetResult();
        await pending;

        // Then
        Assert.Same(before, duringCall);
        Assert.Same(before, Activity.Current);
    }

    [Fact]
    public async Task GivenListener_WhenHandlerIsMissing_ThenAwaitingThrowsInvalidOperation()
    {
        // Given
        var sender = new ServiceCollection().AddSendr().BuildServiceProvider().GetRequiredService<ISender>();

        // When / Then
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new MissingRequest(), default));
    }
}

public sealed record TracedRequest : IRequest;

public sealed class TracedRequestHandler : IRequestHandler<TracedRequest>
{
    public Task HandleAsync(TracedRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record TracedQuery : IRequest<string>;

public sealed class TracedQueryHandler : IRequestHandler<TracedQuery, string>
{
    public Task<string> HandleAsync(TracedQuery request, CancellationToken cancellationToken)
        => Task.FromResult("query-result");
}

public sealed record TracedCommand : ICommand;

public sealed class TracedCommandHandler : ICommandHandler<TracedCommand>
{
    public Task HandleAsync(TracedCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record TracedCommandWithResponse : ICommand<int>;

public sealed class TracedCommandWithResponseHandler : ICommandHandler<TracedCommandWithResponse, int>
{
    public Task<int> HandleAsync(TracedCommandWithResponse command, CancellationToken cancellationToken)
        => Task.FromResult(42);
}

public sealed record TracedQ : IQuery<string>;

public sealed class TracedQHandler : IQueryHandler<TracedQ, string>
{
    public Task<string> HandleAsync(TracedQ query, CancellationToken cancellationToken)
        => Task.FromResult("q-result");
}

public sealed record FailingRequest : IRequest;

public sealed class FailingRequestHandler : IRequestHandler<FailingRequest>
{
    public async Task HandleAsync(FailingRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("boom");
    }
}

public sealed record ThrowsSynchronouslyRequest : IRequest;

public sealed class ThrowsSynchronouslyHandler : IRequestHandler<ThrowsSynchronouslyRequest>
{
    public Task HandleAsync(ThrowsSynchronouslyRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("sync boom");
}

public sealed record GatedRequest(Task Gate) : IRequest;

public sealed class GatedRequestHandler : IRequestHandler<GatedRequest>
{
    public Task HandleAsync(GatedRequest request, CancellationToken cancellationToken) => request.Gate;
}

public sealed record MissingRequest : IRequest;

public sealed record CanceledRequest : IRequest;

public sealed class CanceledRequestHandler : IRequestHandler<CanceledRequest>
{
    public Task HandleAsync(CanceledRequest request, CancellationToken cancellationToken)
        => throw new OperationCanceledException();
}
