using System.Diagnostics;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace Generator.Tests;

// ActivityListener subscriptions are process-wide, so every assertion filters on request types
// declared at the bottom of this file that no other test class dispatches.
public class GeneratedActivityTests : IDisposable
{
    private readonly List<Activity> _stopped = [];
    private readonly ActivityListener _listener;

    public GeneratedActivityTests()
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

    private static ISender CreateGeneratedSender()
    {
        var sender = new ServiceCollection()
            .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        Assert.Equal("GeneratedSender", sender.GetType().Name);
        return sender;
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendQuery_ThenActivityIsEmitted()
    {
        // Given
        var sender = CreateGeneratedSender();

        // When
        var result = await sender.SendAsync(new ActivityGenQuery(), default);

        // Then
        Assert.IsType<ActivityGenDto>(result);
        var activity = Assert.Single(ActivitiesFor(typeof(ActivityGenQuery)));
        Assert.Equal("Send ActivityGenQuery", activity.DisplayName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal("query", activity.GetTagItem("sendr.kind"));
        Assert.True(activity.IsStopped);
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendCommand_ThenActivityIsEmitted()
    {
        // Given
        var sender = CreateGeneratedSender();

        // When
        await sender.SendAsync(new ActivityGenCommand(), default);

        // Then
        var activity = Assert.Single(ActivitiesFor(typeof(ActivityGenCommand)));
        Assert.Equal("command", activity.GetTagItem("sendr.kind"));
    }

    [Fact]
    public async Task GivenGeneratedSender_WhenSendRequestAndCommandWithResponse_ThenActivitiesAreEmitted()
    {
        // Given
        var sender = CreateGeneratedSender();

        // When
        await sender.SendAsync(new ActivityGenRequest(), default);
        var request = await sender.SendAsync(new ActivityGenRequestWithResponse(), default);
        var command = await sender.SendAsync(new ActivityGenCommandWithResponse(), default);

        // Then
        Assert.Equal("ok", request);
        Assert.Equal(7, command);
        Assert.Equal("request", Assert.Single(ActivitiesFor(typeof(ActivityGenRequest))).GetTagItem("sendr.kind"));
        Assert.Equal("request", Assert.Single(ActivitiesFor(typeof(ActivityGenRequestWithResponse))).GetTagItem("sendr.kind"));
        Assert.Equal("command", Assert.Single(ActivitiesFor(typeof(ActivityGenCommandWithResponse))).GetTagItem("sendr.kind"));
    }

    [Theory]
    [InlineData(NotificationRunMode.Sequence, "sequence")]
    [InlineData(NotificationRunMode.Parallel, "parallel")]
    public async Task GivenGeneratedPublisher_WhenPublish_ThenPublishAndHandlerSpansAreEmitted(
        NotificationRunMode runMode, string expectedGroup)
    {
        // Given
        var publisher = new ServiceCollection()
            .AddSendrNotification(o => o.UseGenerators(x => x.RunAs(runMode).IncludeAssemblyOf<HandlerLibraryMarker>()))
            .BuildServiceProvider()
            .GetRequiredService<IPublisher>();
        Assert.Equal("GeneratedPublisher", publisher.GetType().Name);

        // When
        await publisher.PublishAsync(new ActivityGenNote(), default);

        // Then
        Activity[] spans;
        lock (_stopped)
            spans = _stopped.ToArray();

        var publish = Assert.Single(spans, a =>
            (string?)a.GetTagItem("sendr.kind") == "notification"
            && (string?)a.GetTagItem("sendr.request.type") == typeof(ActivityGenNote).FullName);
        Assert.Equal("Publish ActivityGenNote", publish.DisplayName);

        foreach (var handlerType in new[] { typeof(ActivityGenNoteHandlerA), typeof(ActivityGenNoteHandlerB) })
        {
            var handler = Assert.Single(spans, a =>
                (string?)a.GetTagItem("sendr.kind") == "notification.handler"
                && (string?)a.GetTagItem("sendr.handler.type") == handlerType.FullName);
            Assert.Equal("Handle " + handlerType.Name, handler.DisplayName);
            Assert.Equal(expectedGroup, handler.GetTagItem("sendr.notification.group"));
            Assert.Equal(publish.SpanId, handler.ParentSpanId);
        }
    }

    [Fact]
    public async Task GivenGeneratedPublisherAndFailingHandler_WhenPublish_ThenSpansAreMarkedError()
    {
        // Given
        var publisher = new ServiceCollection()
            .AddSendrNotification(o => o.UseGenerators(x => x.IncludeAssemblyOf<HandlerLibraryMarker>()))
            .BuildServiceProvider()
            .GetRequiredService<IPublisher>();

        // When
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new ActivityGenFailingNote(), default));

        // Then
        Activity[] spans;
        lock (_stopped)
            spans = _stopped.ToArray();

        var publish = Assert.Single(spans, a =>
            (string?)a.GetTagItem("sendr.request.type") == typeof(ActivityGenFailingNote).FullName);
        Assert.Equal(ActivityStatusCode.Error, publish.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, publish.GetTagItem("error.type"));
        var handler = Assert.Single(spans, a =>
            (string?)a.GetTagItem("sendr.handler.type") == typeof(ActivityGenFailingNoteHandler).FullName);
        Assert.Equal(ActivityStatusCode.Error, handler.Status);
    }
}

public sealed record ActivityGenQuery : IQuery<ActivityGenDto>;

public sealed record ActivityGenDto;

public sealed class ActivityGenQueryHandler : IQueryHandler<ActivityGenQuery, ActivityGenDto>
{
    public Task<ActivityGenDto> HandleAsync(ActivityGenQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new ActivityGenDto());
}

public sealed record ActivityGenCommand : ICommand;

public sealed class ActivityGenCommandHandler : ICommandHandler<ActivityGenCommand>
{
    public Task HandleAsync(ActivityGenCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActivityGenRequest : IRequest;

public sealed class ActivityGenRequestHandler : IRequestHandler<ActivityGenRequest>
{
    public Task HandleAsync(ActivityGenRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActivityGenRequestWithResponse : IRequest<string>;

public sealed class ActivityGenRequestWithResponseHandler : IRequestHandler<ActivityGenRequestWithResponse, string>
{
    public Task<string> HandleAsync(ActivityGenRequestWithResponse request, CancellationToken cancellationToken)
        => Task.FromResult("ok");
}

public sealed record ActivityGenCommandWithResponse : ICommand<int>;

public sealed class ActivityGenCommandWithResponseHandler : ICommandHandler<ActivityGenCommandWithResponse, int>
{
    public Task<int> HandleAsync(ActivityGenCommandWithResponse command, CancellationToken cancellationToken)
        => Task.FromResult(7);
}

public sealed record ActivityGenNote : INotification;

public sealed class ActivityGenNoteHandlerA : INotificationHandler<ActivityGenNote>
{
    public Task HandleAsync(ActivityGenNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ActivityGenNoteHandlerB : INotificationHandler<ActivityGenNote>
{
    public Task HandleAsync(ActivityGenNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ActivityGenFailingNote : INotification;

public sealed class ActivityGenFailingNoteHandler : INotificationHandler<ActivityGenFailingNote>
{
    public Task HandleAsync(ActivityGenFailingNote notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("gen-boom");
}
