using System.Runtime.CompilerServices;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

// Compiled into both test projects. In Sendr.Generators.Tests the generator discovers every handler below, so
// each message type has exactly one handler and per-test behaviour comes from the DispatchProbe the test
// registers, not from separate handler types.

/// <summary>What one test observed while its message was dispatched.</summary>
public sealed class DispatchProbe
{
    private readonly Lock _gate = new();

    public List<object> HandlersConstructed { get; } = [];
    public List<object> DecoratorsConstructed { get; } = [];
    public List<string> Handled { get; } = [];
    public List<(string Step, CancellationToken Token)> Tokens { get; } = [];
    public List<ScopedMarker> ScopedSeen { get; } = [];

    /// <summary>When set, request handlers fail with it, and stream handlers fail with it after <see cref="FailAfterItems"/> items.</summary>
    public Exception? Failure { get; set; }

    public int FailAfterItems { get; set; }

    public void Record<T>(List<T> list, T item)
    {
        lock (_gate)
            list.Add(item);
    }
}

/// <summary>Registered as scoped, so a handler's copy tells which scope it was resolved from.</summary>
public sealed class ScopedMarker;

public sealed class ProbeFailure() : Exception("Raised by a probe handler");

// ---- Messages: each concrete type derives from an abstract base so tests can send it through the base type ----

public abstract record ProbeRequestBase(int Value) : IRequest;
public sealed record ProbeRequest(int Value) : ProbeRequestBase(Value);

public abstract record ProbeRequestForValueBase(int Value) : IRequest<int>;
public sealed record ProbeRequestForValue(int Value) : ProbeRequestForValueBase(Value);

public abstract record ProbeCommandBase(int Value) : ICommand;
public sealed record ProbeCommand(int Value) : ProbeCommandBase(Value);

public abstract record ProbeCommandForValueBase(int Value) : ICommand<int>;
public sealed record ProbeCommandForValue(int Value) : ProbeCommandForValueBase(Value);

public abstract record ProbeQueryBase(int Value) : IQuery<int>;
public sealed record ProbeQuery(int Value) : ProbeQueryBase(Value);

public abstract record ProbeStreamBase(int Count) : IStreamRequest<int>;
public sealed record ProbeStream(int Count) : ProbeStreamBase(Count);

// One message type, two response types: each response type has its own handler.
public sealed record DualRequest(int Value) : IRequest<int>, IRequest<string>;
public sealed record DualCommand(int Value) : ICommand<int>, ICommand<string>;
public sealed record DualQuery(int Value) : IQuery<int>, IQuery<string>;
public sealed record DualStream(int Count) : IStreamRequest<int>, IStreamRequest<string>;

// No handler exists for these anywhere, in either test project.
public sealed record OrphanRequest : IRequest;
public sealed record OrphanRequestForValue : IRequest<int>;
public sealed record OrphanCommand : ICommand;
public sealed record OrphanCommandForValue : ICommand<int>;
public sealed record OrphanQuery : IQuery<int>;
public sealed record OrphanStream : IStreamRequest<int>;

// ---- Probe handlers ----

public abstract class ProbeHandler
{
    private readonly DispatchProbe _probe;
    private readonly ScopedMarker _scoped;

    protected ProbeHandler(DispatchProbe probe, ScopedMarker scoped)
    {
        _probe = probe;
        _scoped = scoped;
        probe.Record(probe.HandlersConstructed, this);
    }

    protected async Task Handle(CancellationToken cancellationToken)
    {
        _probe.Record(_probe.Handled, GetType().Name);
        _probe.Record(_probe.Tokens, ("handler", cancellationToken));
        _probe.Record(_probe.ScopedSeen, _scoped);
        await Task.Yield();
        if (_probe.Failure is { } failure)
            throw failure;
    }

    protected async IAsyncEnumerable<TItem> Stream<TItem>(int count, Func<int, TItem> item, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _probe.Record(_probe.Handled, GetType().Name);
        _probe.Record(_probe.Tokens, ("handler", cancellationToken));
        _probe.Record(_probe.ScopedSeen, _scoped);
        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_probe.Failure is { } failure && i > _probe.FailAfterItems)
                throw failure;
            await Task.Yield();
            yield return item(i);
        }
    }
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeRequestHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), IRequestHandler<ProbeRequest>
{
    public Task HandleAsync(ProbeRequest request, CancellationToken cancellationToken) => Handle(cancellationToken);
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeRequestForValueHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), IRequestHandler<ProbeRequestForValue, int>
{
    public async Task<int> HandleAsync(ProbeRequestForValue request, CancellationToken cancellationToken)
    {
        await Handle(cancellationToken);
        return request.Value * 2;
    }
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeCommandHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), ICommandHandler<ProbeCommand>
{
    public Task HandleAsync(ProbeCommand command, CancellationToken cancellationToken) => Handle(cancellationToken);
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeCommandForValueHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), ICommandHandler<ProbeCommandForValue, int>
{
    public async Task<int> HandleAsync(ProbeCommandForValue command, CancellationToken cancellationToken)
    {
        await Handle(cancellationToken);
        return command.Value * 2;
    }
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeQueryHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), IQueryHandler<ProbeQuery, int>
{
    public async Task<int> HandleAsync(ProbeQuery query, CancellationToken cancellationToken)
    {
        await Handle(cancellationToken);
        return query.Value * 2;
    }
}

[DecorateWith<ProbeOuterDecorator, ProbeInnerDecorator>]
public sealed class ProbeStreamHandler(DispatchProbe probe, ScopedMarker scoped)
    : ProbeHandler(probe, scoped), IStreamRequestHandler<ProbeStream, int>
{
    public IAsyncEnumerable<int> HandleAsync(ProbeStream request, CancellationToken cancellationToken)
        => Stream(request.Count, i => i, cancellationToken);
}

public sealed class DualRequestIntHandler : IRequestHandler<DualRequest, int>
{
    public async Task<int> HandleAsync(DualRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value;
    }
}

public sealed class DualRequestStringHandler : IRequestHandler<DualRequest, string>
{
    public async Task<string> HandleAsync(DualRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return $"#{request.Value}";
    }
}

public sealed class DualCommandIntHandler : ICommandHandler<DualCommand, int>
{
    public async Task<int> HandleAsync(DualCommand command, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return command.Value;
    }
}

public sealed class DualCommandStringHandler : ICommandHandler<DualCommand, string>
{
    public async Task<string> HandleAsync(DualCommand command, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return $"#{command.Value}";
    }
}

public sealed class DualQueryIntHandler : IQueryHandler<DualQuery, int>
{
    public async Task<int> HandleAsync(DualQuery query, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return query.Value;
    }
}

public sealed class DualQueryStringHandler : IQueryHandler<DualQuery, string>
{
    public async Task<string> HandleAsync(DualQuery query, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return $"#{query.Value}";
    }
}

public sealed class DualStreamIntHandler : IStreamRequestHandler<DualStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(DualStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class DualStreamStringHandler : IStreamRequestHandler<DualStream, string>
{
    public async IAsyncEnumerable<string> HandleAsync(DualStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return $"#{i}";
        }
    }
}

// ---- Probe decorators: one type per position, each usable on every handler kind ----

public abstract class ProbeDecorator :
    IRequestDecorator,
    IRequestDecorator.WithResponse,
    IStreamRequestDecorator,
    ICommandDecorator,
    ICommandDecorator.WithResponse,
    IQueryDecorator
{
    private readonly DispatchProbe _probe;
    private readonly string _step;

    protected ProbeDecorator(DispatchProbe probe, string step)
    {
        _probe = probe;
        _step = step;
        probe.Record(probe.DecoratorsConstructed, this);
    }

    private void Record(CancellationToken cancellationToken) => _probe.Record(_probe.Tokens, (_step, cancellationToken));

    Task IRequestDecorator.HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }

    Task<TResponse> IRequestDecorator.WithResponse.HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }

    IAsyncEnumerable<TResponse> IStreamRequestDecorator.HandleAsync<TRequest, TResponse>(
        TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }

    Task ICommandDecorator.HandleAsync<TCommand>(TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }

    Task<TResponse> ICommandDecorator.WithResponse.HandleAsync<TCommand, TResponse>(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }

    Task<TResponse> IQueryDecorator.HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record(cancellationToken);
        return next();
    }
}

public sealed class ProbeOuterDecorator(DispatchProbe probe) : ProbeDecorator(probe, "outer decorator");

public sealed class ProbeInnerDecorator(DispatchProbe probe) : ProbeDecorator(probe, "inner decorator");

// ---- Registration ----

public static class DispatchProbeRegistration
{
    public static IServiceCollection AddDispatchProbe(this IServiceCollection services, DispatchProbe probe) =>
        services.AddSingleton(probe).AddScoped<ScopedMarker>();

    /// <summary>
    /// Registers the probe handlers and their decorators by hand, in the same order the
    /// [DecorateWith] attributes declare them, for the reflection-based sender.
    /// </summary>
    public static IServiceCollection AddProbeHandlersManually(this IServiceCollection services) => services
        .AddRequestHandler<ProbeRequest, ProbeRequestHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddRequestHandler<ProbeRequestForValue, int, ProbeRequestForValueHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddCommandHandler<ProbeCommand, ProbeCommandHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddCommandHandler<ProbeCommandForValue, int, ProbeCommandForValueHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddQueryHandler<ProbeQuery, int, ProbeQueryHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddStreamRequestHandler<ProbeStream, int, ProbeStreamHandler>(x => x.Decorator.With<ProbeOuterDecorator>().With<ProbeInnerDecorator>())
        .AddRequestHandler<DualRequest, int, DualRequestIntHandler>()
        .AddRequestHandler<DualRequest, string, DualRequestStringHandler>()
        .AddCommandHandler<DualCommand, int, DualCommandIntHandler>()
        .AddCommandHandler<DualCommand, string, DualCommandStringHandler>()
        .AddQueryHandler<DualQuery, int, DualQueryIntHandler>()
        .AddQueryHandler<DualQuery, string, DualQueryStringHandler>()
        .AddStreamRequestHandler<DualStream, int, DualStreamIntHandler>()
        .AddStreamRequestHandler<DualStream, string, DualStreamStringHandler>();
}

// ---- The five SendAsync overloads, as data for theories ----

/// <summary>One SendAsync overload, with every way the request-dispatch tests need to call it.</summary>
public sealed record DispatchCase(
    string Overload,
    string HandlerName,
    bool HasResponse,
    string NullParamName,
    string RegistrationMethod,
    Type OrphanType,
    Func<ISender, int, CancellationToken, Task<object?>> SendConcrete,
    Func<ISender, int, CancellationToken, Task<object?>> SendAsInterface,
    Func<ISender, int, CancellationToken, Task<object?>> SendAsBaseType,
    Func<ISender, CancellationToken, Task> SendNull,
    Func<ISender, CancellationToken, Task> SendOrphan);

public static class DispatchCases
{
    public static readonly IReadOnlyDictionary<string, DispatchCase> All = new[]
    {
        new DispatchCase(
            "SendAsync(IRequest)", nameof(ProbeRequestHandler), HasResponse: false, "request", "AddRequestHandler", typeof(OrphanRequest),
            async (s, v, ct) => { await s.SendAsync(new ProbeRequest(v), ct); return null; },
            async (s, v, ct) => { IRequest m = new ProbeRequest(v); await s.SendAsync(m, ct); return null; },
            async (s, v, ct) => { ProbeRequestBase m = new ProbeRequest(v); await s.SendAsync(m, ct); return null; },
            (s, ct) => s.SendAsync((IRequest)null!, ct),
            (s, ct) => s.SendAsync(new OrphanRequest(), ct)),
        new DispatchCase(
            "SendAsync(IRequest<T>)", nameof(ProbeRequestForValueHandler), HasResponse: true, "request", "AddRequestHandler", typeof(OrphanRequestForValue),
            async (s, v, ct) => await s.SendAsync(new ProbeRequestForValue(v), ct),
            async (s, v, ct) => { IRequest<int> m = new ProbeRequestForValue(v); return await s.SendAsync(m, ct); },
            async (s, v, ct) => { ProbeRequestForValueBase m = new ProbeRequestForValue(v); return await s.SendAsync(m, ct); },
            (s, ct) => s.SendAsync((IRequest<int>)null!, ct),
            (s, ct) => s.SendAsync(new OrphanRequestForValue(), ct)),
        new DispatchCase(
            "SendAsync(ICommand)", nameof(ProbeCommandHandler), HasResponse: false, "command", "AddCommandHandler", typeof(OrphanCommand),
            async (s, v, ct) => { await s.SendAsync(new ProbeCommand(v), ct); return null; },
            async (s, v, ct) => { ICommand m = new ProbeCommand(v); await s.SendAsync(m, ct); return null; },
            async (s, v, ct) => { ProbeCommandBase m = new ProbeCommand(v); await s.SendAsync(m, ct); return null; },
            (s, ct) => s.SendAsync((ICommand)null!, ct),
            (s, ct) => s.SendAsync(new OrphanCommand(), ct)),
        new DispatchCase(
            "SendAsync(ICommand<T>)", nameof(ProbeCommandForValueHandler), HasResponse: true, "command", "AddCommandHandler", typeof(OrphanCommandForValue),
            async (s, v, ct) => await s.SendAsync(new ProbeCommandForValue(v), ct),
            async (s, v, ct) => { ICommand<int> m = new ProbeCommandForValue(v); return await s.SendAsync(m, ct); },
            async (s, v, ct) => { ProbeCommandForValueBase m = new ProbeCommandForValue(v); return await s.SendAsync(m, ct); },
            (s, ct) => s.SendAsync((ICommand<int>)null!, ct),
            (s, ct) => s.SendAsync(new OrphanCommandForValue(), ct)),
        new DispatchCase(
            "SendAsync(IQuery<T>)", nameof(ProbeQueryHandler), HasResponse: true, "query", "AddQueryHandler", typeof(OrphanQuery),
            async (s, v, ct) => await s.SendAsync(new ProbeQuery(v), ct),
            async (s, v, ct) => { IQuery<int> m = new ProbeQuery(v); return await s.SendAsync(m, ct); },
            async (s, v, ct) => { ProbeQueryBase m = new ProbeQuery(v); return await s.SendAsync(m, ct); },
            (s, ct) => s.SendAsync((IQuery<int>)null!, ct),
            (s, ct) => s.SendAsync(new OrphanQuery(), ct)),
    }.ToDictionary(c => c.Overload);

    public static TheoryData<string> AllOverloads => new(All.Keys);

    public static TheoryData<string> WithoutResponse => new(All.Values.Where(c => !c.HasResponse).Select(c => c.Overload));

    public static TheoryData<string> WithResponse => new(All.Values.Where(c => c.HasResponse).Select(c => c.Overload));
}
