using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

// Compiled into both test projects. For the generated sender, a handler's decorators are fixed at compile
// time by [DecorateWith], so every decorator arrangement the decorators and handler-lifetime scenarios need
// gets its own set of messages and handlers: one per dispatch kind (five SendAsync overloads, SendStream,
// PublishAsync). The behaviour lives in the generic bases; each arrangement only declares closed types.

/// <summary>What one test observed while its message went through a decorator pipeline.</summary>
public sealed class PipelineProbe
{
    private readonly Lock _gate = new();
    private readonly Dictionary<object, int> _disposals = new(ReferenceEqualityComparer.Instance);
    private int _handleCalls;

    /// <summary>"before" / "after" entries from decorators and an entry per handler run, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Which decorator interface's HandleAsync ran, once per call.</summary>
    public List<string> DecoratorInterfaces { get; } = [];

    public List<object> HandlersConstructed { get; } = [];
    public List<object> DecoratorsConstructed { get; } = [];

    /// <summary>Thrown by <see cref="FaultyDecorator"/>'s constructor.</summary>
    public Exception DecoratorFailure { get; } = new DecoratorConstructionFailure();

    public int HandleCalls => Volatile.Read(ref _handleCalls);

    public void Write(string entry)
    {
        lock (_gate)
            Log.Add(entry);
    }

    public void Record<T>(List<T> list, T item)
    {
        lock (_gate)
            list.Add(item);
    }

    public void CountHandle() => Interlocked.Increment(ref _handleCalls);

    public void Disposed(object instance)
    {
        lock (_gate)
            _disposals[instance] = _disposals.GetValueOrDefault(instance) + 1;
    }

    public int DisposalsOf(object instance)
    {
        lock (_gate)
            return _disposals.GetValueOrDefault(instance);
    }
}

public sealed class DecoratorConstructionFailure() : Exception("Raised by a decorator constructor");

// ---- Handler bases: one per dispatch kind ----

public abstract class PipelineHandler
{
    protected PipelineHandler(PipelineProbe probe)
    {
        Probe = probe;
        probe.Record(probe.HandlersConstructed, this);
    }

    protected PipelineProbe Probe { get; }

    /// <summary>The log entry written when this handler runs.</summary>
    protected virtual string LogName => "handler";

    protected async Task Run()
    {
        Probe.CountHandle();
        Probe.Write(LogName);
        await Task.Yield();
    }

    protected async IAsyncEnumerable<int> Items()
    {
        Probe.CountHandle();
        for (var i = 1; i <= 2; i++)
        {
            await Task.Yield();
            Probe.Write($"{LogName} {i}");
            yield return i;
        }
    }

    protected void Disposed() => Probe.Disposed(this);

    protected ValueTask DisposedAsync()
    {
        Probe.Disposed(this);
        return ValueTask.CompletedTask;
    }
}

public abstract class PipelineRequestHandler<TRequest>(PipelineProbe probe) : PipelineHandler(probe), IRequestHandler<TRequest>
    where TRequest : IRequest
{
    public Task HandleAsync(TRequest request, CancellationToken cancellationToken) => Run();
}

public abstract class PipelineRequestForValueHandler<TRequest>(PipelineProbe probe) : PipelineHandler(probe), IRequestHandler<TRequest, int>
    where TRequest : IRequest<int>
{
    public async Task<int> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        await Run();
        return PipelineCases.HandlerResponse;
    }
}

public abstract class PipelineCommandHandler<TCommand>(PipelineProbe probe) : PipelineHandler(probe), ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public Task HandleAsync(TCommand command, CancellationToken cancellationToken) => Run();
}

public abstract class PipelineCommandForValueHandler<TCommand>(PipelineProbe probe) : PipelineHandler(probe), ICommandHandler<TCommand, int>
    where TCommand : ICommand<int>
{
    public async Task<int> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        await Run();
        return PipelineCases.HandlerResponse;
    }
}

public abstract class PipelineQueryHandler<TQuery>(PipelineProbe probe) : PipelineHandler(probe), IQueryHandler<TQuery, int>
    where TQuery : IQuery<int>
{
    public async Task<int> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        await Run();
        return PipelineCases.HandlerResponse;
    }
}

public abstract class PipelineStreamHandler<TRequest>(PipelineProbe probe) : PipelineHandler(probe), IStreamRequestHandler<TRequest, int>
    where TRequest : IStreamRequest<int>
{
    public IAsyncEnumerable<int> HandleAsync(TRequest request, CancellationToken cancellationToken) => Items();
}

public abstract class PipelineNotificationHandler<TNotification>(PipelineProbe probe) : PipelineHandler(probe), INotificationHandler<TNotification>
    where TNotification : INotification
{
    public Task HandleAsync(TNotification notification, CancellationToken cancellationToken) => Run();
}

// ---- Decorators: each implements every decorator interface, so one type fits every handler kind ----

public abstract class PipelineDecorator :
    IRequestDecorator,
    IRequestDecorator.WithResponse,
    IStreamRequestDecorator,
    ICommandDecorator,
    ICommandDecorator.WithResponse,
    IQueryDecorator,
    INotificationDecorator
{
    private readonly string _name;

    protected PipelineDecorator(PipelineProbe probe, string name)
    {
        Probe = probe;
        _name = name;
        probe.Record(probe.DecoratorsConstructed, this);
    }

    protected PipelineProbe Probe { get; }

    /// <summary>Lets a decorator change the int response it passes back; the default passes it through.</summary>
    protected virtual int Adjust(int response) => response;

    private async Task Around(string decoratorInterface, Func<Task> next)
    {
        Probe.Record(Probe.DecoratorInterfaces, decoratorInterface);
        Probe.Write($"{_name} before");
        await next();
        Probe.Write($"{_name} after");
    }

    private async Task<TResponse> Around<TResponse>(string decoratorInterface, RequestHandlerDelegate<TResponse> next)
    {
        Probe.Record(Probe.DecoratorInterfaces, decoratorInterface);
        Probe.Write($"{_name} before");
        var response = await next();
        if (response is int value)
            response = (TResponse)(object)Adjust(value);
        Probe.Write($"{_name} after");
        return response;
    }

    private async IAsyncEnumerable<TResponse> AroundStream<TResponse>(StreamHandlerDelegate<TResponse> next)
    {
        Probe.Record(Probe.DecoratorInterfaces, nameof(IStreamRequestDecorator));
        Probe.Write($"{_name} before");
        await foreach (var item in next())
            yield return item;
        Probe.Write($"{_name} after");
    }

    Task IRequestDecorator.HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        => Around(nameof(IRequestDecorator), () => next());

    Task<TResponse> IRequestDecorator.WithResponse.HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Around("IRequestDecorator.WithResponse", next);

    IAsyncEnumerable<TResponse> IStreamRequestDecorator.HandleAsync<TRequest, TResponse>(
        TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => AroundStream(next);

    Task ICommandDecorator.HandleAsync<TCommand>(TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
        => Around(nameof(ICommandDecorator), () => next());

    Task<TResponse> ICommandDecorator.WithResponse.HandleAsync<TCommand, TResponse>(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Around("ICommandDecorator.WithResponse", next);

    Task<TResponse> IQueryDecorator.HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Around(nameof(IQueryDecorator), next);

    Task INotificationDecorator.HandleAsync<TNotification>(
        TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
        => Around(nameof(INotificationDecorator), () => next());
}

/// <summary>Records around the handler and passes back the handler's response plus 1000.</summary>
public sealed class SingleDecorator(PipelineProbe probe) : PipelineDecorator(probe, "decorator")
{
    protected override int Adjust(int response)
    {
        Probe.Write($"decorator got {response}");
        return response + PipelineCases.DecoratorOffset;
    }
}

public sealed class OuterDecorator(PipelineProbe probe) : PipelineDecorator(probe, "A");

public sealed class InnerDecorator(PipelineProbe probe) : PipelineDecorator(probe, "B");

public sealed class DisposableDecorator(PipelineProbe probe) : PipelineDecorator(probe, "decorator"), IDisposable
{
    public void Dispose() => Probe.Disposed(this);
}

public sealed class FaultyDecorator : PipelineDecorator
{
    public FaultyDecorator(PipelineProbe probe) : base(probe, "faulty") => throw probe.DecoratorFailure;
}

/// <summary>Never calls next: answers -1, yields a single -1, or just completes.</summary>
public sealed class ShortCircuitDecorator(PipelineProbe probe) :
    IRequestDecorator,
    IRequestDecorator.WithResponse,
    IStreamRequestDecorator,
    ICommandDecorator,
    ICommandDecorator.WithResponse,
    IQueryDecorator,
    INotificationDecorator
{
    private Task Skip()
    {
        probe.Write("short-circuit");
        return Task.CompletedTask;
    }

    private Task<TResponse> Answer<TResponse>()
    {
        probe.Write("short-circuit");
        return Task.FromResult((TResponse)(object)PipelineCases.ShortCircuitResponse);
    }

    private async IAsyncEnumerable<TResponse> AnswerStream<TResponse>()
    {
        probe.Write("short-circuit");
        await Task.Yield();
        yield return (TResponse)(object)PipelineCases.ShortCircuitResponse;
    }

    Task IRequestDecorator.HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken) => Skip();

    Task<TResponse> IRequestDecorator.WithResponse.HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => Answer<TResponse>();

    IAsyncEnumerable<TResponse> IStreamRequestDecorator.HandleAsync<TRequest, TResponse>(
        TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => AnswerStream<TResponse>();

    Task ICommandDecorator.HandleAsync<TCommand>(TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken) => Skip();

    Task<TResponse> ICommandDecorator.WithResponse.HandleAsync<TCommand, TResponse>(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => Answer<TResponse>();

    Task<TResponse> IQueryDecorator.HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => Answer<TResponse>();

    Task INotificationDecorator.HandleAsync<TNotification>(
        TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken) => Skip();
}

// ---- Single: One decorator that records around the handler and changes the response it passes back. ----

public sealed record SingleRequest : IRequest;

[DecorateWith<SingleDecorator>]
public sealed class SingleRequestHandler(PipelineProbe probe) : PipelineRequestHandler<SingleRequest>(probe);

public sealed record SingleRequestForValue : IRequest<int>;

[DecorateWith<SingleDecorator>]
public sealed class SingleRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<SingleRequestForValue>(probe);

public sealed record SingleCommand : ICommand;

[DecorateWith<SingleDecorator>]
public sealed class SingleCommandHandler(PipelineProbe probe) : PipelineCommandHandler<SingleCommand>(probe);

public sealed record SingleCommandForValue : ICommand<int>;

[DecorateWith<SingleDecorator>]
public sealed class SingleCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<SingleCommandForValue>(probe);

public sealed record SingleQuery : IQuery<int>;

[DecorateWith<SingleDecorator>]
public sealed class SingleQueryHandler(PipelineProbe probe) : PipelineQueryHandler<SingleQuery>(probe);

public sealed record SingleStream : IStreamRequest<int>;

[DecorateWith<SingleDecorator>]
public sealed class SingleStreamHandler(PipelineProbe probe) : PipelineStreamHandler<SingleStream>(probe);

public sealed record SingleNote : INotification;

[DecorateWith<SingleDecorator>]
public sealed class SingleNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<SingleNote>(probe);

// ---- Pair: Two decorators, declared outer first. ----

public sealed record PairRequest : IRequest;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairRequestHandler(PipelineProbe probe) : PipelineRequestHandler<PairRequest>(probe);

public sealed record PairRequestForValue : IRequest<int>;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<PairRequestForValue>(probe);

public sealed record PairCommand : ICommand;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairCommandHandler(PipelineProbe probe) : PipelineCommandHandler<PairCommand>(probe);

public sealed record PairCommandForValue : ICommand<int>;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<PairCommandForValue>(probe);

public sealed record PairQuery : IQuery<int>;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairQueryHandler(PipelineProbe probe) : PipelineQueryHandler<PairQuery>(probe);

public sealed record PairStream : IStreamRequest<int>;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairStreamHandler(PipelineProbe probe) : PipelineStreamHandler<PairStream>(probe);

public sealed record PairNote : INotification;

[DecorateWith<OuterDecorator, InnerDecorator>]
public sealed class PairNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<PairNote>(probe);

// ---- Short: A decorator that never calls next. ----

public sealed record ShortRequest : IRequest;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortRequestHandler(PipelineProbe probe) : PipelineRequestHandler<ShortRequest>(probe);

public sealed record ShortRequestForValue : IRequest<int>;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<ShortRequestForValue>(probe);

public sealed record ShortCommand : ICommand;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortCommandHandler(PipelineProbe probe) : PipelineCommandHandler<ShortCommand>(probe);

public sealed record ShortCommandForValue : ICommand<int>;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<ShortCommandForValue>(probe);

public sealed record ShortQuery : IQuery<int>;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortQueryHandler(PipelineProbe probe) : PipelineQueryHandler<ShortQuery>(probe);

public sealed record ShortStream : IStreamRequest<int>;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortStreamHandler(PipelineProbe probe) : PipelineStreamHandler<ShortStream>(probe);

public sealed record ShortNote : INotification;

[DecorateWith<ShortCircuitDecorator>]
public sealed class ShortNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<ShortNote>(probe);

// ---- Plain: Disposable handlers with no decorator. ----

public sealed record PlainRequest : IRequest;

public sealed class PlainRequestHandler(PipelineProbe probe) : PipelineRequestHandler<PlainRequest>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainRequestForValue : IRequest<int>;

public sealed class PlainRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<PlainRequestForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainCommand : ICommand;

public sealed class PlainCommandHandler(PipelineProbe probe) : PipelineCommandHandler<PlainCommand>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainCommandForValue : ICommand<int>;

public sealed class PlainCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<PlainCommandForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainQuery : IQuery<int>;

public sealed class PlainQueryHandler(PipelineProbe probe) : PipelineQueryHandler<PlainQuery>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainStream : IStreamRequest<int>;

public sealed class PlainStreamHandler(PipelineProbe probe) : PipelineStreamHandler<PlainStream>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record PlainNote : INotification;

public sealed class PlainNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<PlainNote>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

// ---- Disp: Disposable handlers wrapped by a disposable decorator. ----

public sealed record DispRequest : IRequest;

[DecorateWith<DisposableDecorator>]
public sealed class DispRequestHandler(PipelineProbe probe) : PipelineRequestHandler<DispRequest>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispRequestForValue : IRequest<int>;

[DecorateWith<DisposableDecorator>]
public sealed class DispRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<DispRequestForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispCommand : ICommand;

[DecorateWith<DisposableDecorator>]
public sealed class DispCommandHandler(PipelineProbe probe) : PipelineCommandHandler<DispCommand>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispCommandForValue : ICommand<int>;

[DecorateWith<DisposableDecorator>]
public sealed class DispCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<DispCommandForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispQuery : IQuery<int>;

[DecorateWith<DisposableDecorator>]
public sealed class DispQueryHandler(PipelineProbe probe) : PipelineQueryHandler<DispQuery>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispStream : IStreamRequest<int>;

[DecorateWith<DisposableDecorator>]
public sealed class DispStreamHandler(PipelineProbe probe) : PipelineStreamHandler<DispStream>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record DispNote : INotification;

[DecorateWith<DisposableDecorator>]
public sealed class DispNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<DispNote>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

// ---- AsyncOnly: Handlers that only implement IAsyncDisposable, wrapped by a decorator. ----

public sealed record AsyncOnlyRequest : IRequest;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyRequestHandler(PipelineProbe probe) : PipelineRequestHandler<AsyncOnlyRequest>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyRequestForValue : IRequest<int>;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<AsyncOnlyRequestForValue>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyCommand : ICommand;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyCommandHandler(PipelineProbe probe) : PipelineCommandHandler<AsyncOnlyCommand>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyCommandForValue : ICommand<int>;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<AsyncOnlyCommandForValue>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyQuery : IQuery<int>;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyQueryHandler(PipelineProbe probe) : PipelineQueryHandler<AsyncOnlyQuery>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyStream : IStreamRequest<int>;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyStreamHandler(PipelineProbe probe) : PipelineStreamHandler<AsyncOnlyStream>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

public sealed record AsyncOnlyNote : INotification;

[DecorateWith<SingleDecorator>]
public sealed class AsyncOnlyNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<AsyncOnlyNote>(probe), IAsyncDisposable
{
    public ValueTask DisposeAsync() => DisposedAsync();
}

// ---- Faulty: Disposable handlers whose decorator throws while being constructed. ----

public sealed record FaultyRequest : IRequest;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyRequestHandler(PipelineProbe probe) : PipelineRequestHandler<FaultyRequest>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyRequestForValue : IRequest<int>;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyRequestForValueHandler(PipelineProbe probe) : PipelineRequestForValueHandler<FaultyRequestForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyCommand : ICommand;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyCommandHandler(PipelineProbe probe) : PipelineCommandHandler<FaultyCommand>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyCommandForValue : ICommand<int>;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyCommandForValueHandler(PipelineProbe probe) : PipelineCommandForValueHandler<FaultyCommandForValue>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyQuery : IQuery<int>;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyQueryHandler(PipelineProbe probe) : PipelineQueryHandler<FaultyQuery>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyStream : IStreamRequest<int>;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyStreamHandler(PipelineProbe probe) : PipelineStreamHandler<FaultyStream>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

public sealed record FaultyNote : INotification;

[DecorateWith<FaultyDecorator>]
public sealed class FaultyNoteHandler(PipelineProbe probe) : PipelineNotificationHandler<FaultyNote>(probe), IDisposable
{
    public void Dispose() => Disposed();
}

// ---- A notification with one decorated and one plain handler ----

public sealed record PartialNote : INotification;

[DecorateWith<SingleDecorator>]
public sealed class PartialDecoratedHandler(PipelineProbe probe) : PipelineNotificationHandler<PartialNote>(probe)
{
    protected override string LogName => "decorated handler";
}

public sealed class PartialPlainHandler(PipelineProbe probe) : PipelineNotificationHandler<PartialNote>(probe)
{
    protected override string LogName => "plain handler";
}

// ---- Registration ----

public static class PipelineRegistration
{
    /// <summary>
    /// Registers every arrangement's handlers and decorators by hand, in the same order their [DecorateWith]
    /// attributes declare them, for the reflection-based sender and the default publisher. PartialNote is
    /// left out: its tests choose the Sequence or Parallel group themselves.
    /// </summary>
    public static IServiceCollection AddPipelineHandlersManually(this IServiceCollection services) => services
        .AddRequestHandler<SingleRequest, SingleRequestHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddRequestHandler<SingleRequestForValue, int, SingleRequestForValueHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddCommandHandler<SingleCommand, SingleCommandHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddCommandHandler<SingleCommandForValue, int, SingleCommandForValueHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddQueryHandler<SingleQuery, int, SingleQueryHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddStreamRequestHandler<SingleStream, int, SingleStreamHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddNotificationHandler<SingleNote>(x => x.Handler.Sequence.With<SingleNoteHandler>(h => h.Decorator.With<SingleDecorator>()))
        .AddRequestHandler<PairRequest, PairRequestHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddRequestHandler<PairRequestForValue, int, PairRequestForValueHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddCommandHandler<PairCommand, PairCommandHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddCommandHandler<PairCommandForValue, int, PairCommandForValueHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddQueryHandler<PairQuery, int, PairQueryHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddStreamRequestHandler<PairStream, int, PairStreamHandler>(x => x.Decorator.With<OuterDecorator>().With<InnerDecorator>())
        .AddNotificationHandler<PairNote>(x => x.Handler.Sequence.With<PairNoteHandler>(h => h.Decorator.With<OuterDecorator>().With<InnerDecorator>()))
        .AddRequestHandler<ShortRequest, ShortRequestHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddRequestHandler<ShortRequestForValue, int, ShortRequestForValueHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddCommandHandler<ShortCommand, ShortCommandHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddCommandHandler<ShortCommandForValue, int, ShortCommandForValueHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddQueryHandler<ShortQuery, int, ShortQueryHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddStreamRequestHandler<ShortStream, int, ShortStreamHandler>(x => x.Decorator.With<ShortCircuitDecorator>())
        .AddNotificationHandler<ShortNote>(x => x.Handler.Sequence.With<ShortNoteHandler>(h => h.Decorator.With<ShortCircuitDecorator>()))
        .AddRequestHandler<PlainRequest, PlainRequestHandler>()
        .AddRequestHandler<PlainRequestForValue, int, PlainRequestForValueHandler>()
        .AddCommandHandler<PlainCommand, PlainCommandHandler>()
        .AddCommandHandler<PlainCommandForValue, int, PlainCommandForValueHandler>()
        .AddQueryHandler<PlainQuery, int, PlainQueryHandler>()
        .AddStreamRequestHandler<PlainStream, int, PlainStreamHandler>()
        .AddNotificationHandler<PlainNote>(x => x.Handler.Sequence.With<PlainNoteHandler>())
        .AddRequestHandler<DispRequest, DispRequestHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddRequestHandler<DispRequestForValue, int, DispRequestForValueHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddCommandHandler<DispCommand, DispCommandHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddCommandHandler<DispCommandForValue, int, DispCommandForValueHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddQueryHandler<DispQuery, int, DispQueryHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddStreamRequestHandler<DispStream, int, DispStreamHandler>(x => x.Decorator.With<DisposableDecorator>())
        .AddNotificationHandler<DispNote>(x => x.Handler.Sequence.With<DispNoteHandler>(h => h.Decorator.With<DisposableDecorator>()))
        .AddRequestHandler<AsyncOnlyRequest, AsyncOnlyRequestHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddRequestHandler<AsyncOnlyRequestForValue, int, AsyncOnlyRequestForValueHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddCommandHandler<AsyncOnlyCommand, AsyncOnlyCommandHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddCommandHandler<AsyncOnlyCommandForValue, int, AsyncOnlyCommandForValueHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddQueryHandler<AsyncOnlyQuery, int, AsyncOnlyQueryHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddStreamRequestHandler<AsyncOnlyStream, int, AsyncOnlyStreamHandler>(x => x.Decorator.With<SingleDecorator>())
        .AddNotificationHandler<AsyncOnlyNote>(x => x.Handler.Sequence.With<AsyncOnlyNoteHandler>(h => h.Decorator.With<SingleDecorator>()))
        .AddRequestHandler<FaultyRequest, FaultyRequestHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddRequestHandler<FaultyRequestForValue, int, FaultyRequestForValueHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddCommandHandler<FaultyCommand, FaultyCommandHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddCommandHandler<FaultyCommandForValue, int, FaultyCommandForValueHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddQueryHandler<FaultyQuery, int, FaultyQueryHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddStreamRequestHandler<FaultyStream, int, FaultyStreamHandler>(x => x.Decorator.With<FaultyDecorator>())
        .AddNotificationHandler<FaultyNote>(x => x.Handler.Sequence.With<FaultyNoteHandler>(h => h.Decorator.With<FaultyDecorator>()));
}

// ---- The seven dispatch kinds, as data for theories ----

public static class PipelineCases
{
    public const int HandlerResponse = 7;
    public const int DecoratorOffset = 1000;
    public const int ShortCircuitResponse = -1;

    public const string SendStream = "SendStream";
    public const string PublishAsync = "PublishAsync";

    public static readonly string[] SendAsyncKinds =
    [
        "SendAsync(IRequest)",
        "SendAsync(IRequest<T>)",
        "SendAsync(ICommand)",
        "SendAsync(ICommand<T>)",
        "SendAsync(IQuery<T>)",
    ];

    public static readonly string[] AllKinds = [.. SendAsyncKinds, SendStream, PublishAsync];

    public static TheoryData<string> SendAsyncKindData => new(SendAsyncKinds);

    public static TheoryData<string> AllKindData => new(AllKinds);

    public static TheoryData<string> AllKindsButPublishData => new(AllKinds.Where(kind => kind != PublishAsync));

    public static bool HasResponse(string kind) => kind is "SendAsync(IRequest<T>)" or "SendAsync(ICommand<T>)" or "SendAsync(IQuery<T>)";

    /// <summary>The log entries one handler run writes for this kind: a stream handler writes one per item.</summary>
    public static string[] HandlerEntries(string kind) => kind == SendStream ? ["handler 1", "handler 2"] : ["handler"];

    /// <summary>The decorator interface whose HandleAsync serves this kind.</summary>
    public static string DecoratorInterfaceFor(string kind) => kind switch
    {
        "SendAsync(IRequest)" => nameof(IRequestDecorator),
        "SendAsync(IRequest<T>)" => "IRequestDecorator.WithResponse",
        "SendAsync(ICommand)" => nameof(ICommandDecorator),
        "SendAsync(ICommand<T>)" => "ICommandDecorator.WithResponse",
        "SendAsync(IQuery<T>)" => nameof(IQueryDecorator),
        SendStream => nameof(IStreamRequestDecorator),
        _ => nameof(INotificationDecorator),
    };

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Single =
        For<SingleRequest, SingleRequestForValue, SingleCommand, SingleCommandForValue, SingleQuery, SingleStream, SingleNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Pair =
        For<PairRequest, PairRequestForValue, PairCommand, PairCommandForValue, PairQuery, PairStream, PairNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Short =
        For<ShortRequest, ShortRequestForValue, ShortCommand, ShortCommandForValue, ShortQuery, ShortStream, ShortNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Plain =
        For<PlainRequest, PlainRequestForValue, PlainCommand, PlainCommandForValue, PlainQuery, PlainStream, PlainNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Disp =
        For<DispRequest, DispRequestForValue, DispCommand, DispCommandForValue, DispQuery, DispStream, DispNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> AsyncOnly =
        For<AsyncOnlyRequest, AsyncOnlyRequestForValue, AsyncOnlyCommand, AsyncOnlyCommandForValue, AsyncOnlyQuery, AsyncOnlyStream, AsyncOnlyNote>();

    public static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> Faulty =
        For<FaultyRequest, FaultyRequestForValue, FaultyCommand, FaultyCommandForValue, FaultyQuery, FaultyStream, FaultyNote>();

    /// <summary>How to dispatch one arrangement's message of each kind and collect what the caller gets back.</summary>
    public static IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> For<TRequest, TRequestForValue, TCommand, TCommandForValue, TQuery, TStream, TNote>()
        where TRequest : IRequest, new()
        where TRequestForValue : IRequest<int>, new()
        where TCommand : ICommand, new()
        where TCommandForValue : ICommand<int>, new()
        where TQuery : IQuery<int>, new()
        where TStream : IStreamRequest<int>, new()
        where TNote : INotification, new() =>
        new Dictionary<string, Func<IServiceProvider, Task<object?>>>
        {
            ["SendAsync(IRequest)"] = async sp =>
            {
                await sp.GetRequiredService<ISender>().SendAsync((IRequest)new TRequest(), CancellationToken.None);
                return null;
            },
            ["SendAsync(IRequest<T>)"] = async sp =>
                await sp.GetRequiredService<ISender>().SendAsync((IRequest<int>)new TRequestForValue(), CancellationToken.None),
            ["SendAsync(ICommand)"] = async sp =>
            {
                await sp.GetRequiredService<ISender>().SendAsync((ICommand)new TCommand(), CancellationToken.None);
                return null;
            },
            ["SendAsync(ICommand<T>)"] = async sp =>
                await sp.GetRequiredService<ISender>().SendAsync((ICommand<int>)new TCommandForValue(), CancellationToken.None),
            ["SendAsync(IQuery<T>)"] = async sp =>
                await sp.GetRequiredService<ISender>().SendAsync((IQuery<int>)new TQuery(), CancellationToken.None),
            [SendStream] = async sp =>
            {
                var items = new List<int>();
                await foreach (var item in sp.GetRequiredService<IStreamSender>().SendStream(new TStream(), CancellationToken.None))
                    items.Add(item);
                return items;
            },
            [PublishAsync] = async sp =>
            {
                await sp.GetRequiredService<IPublisher>().PublishAsync(new TNote(), CancellationToken.None);
                return null;
            },
        };
}
