using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Davish.Sendr;

/// <summary>
/// Dispatches requests to their registered handlers, running any configured decorator pipeline.
/// Resolve an instance from the service provider after calling <c>AddSendr</c>.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Dispatches a request that does not produce a response value to its handler.
    /// </summary>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    Task SendAsync(IRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a request to its handler and returns the produced response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the request.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the request.</returns>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches stream requests to their registered handlers, running any configured decorator
/// pipeline. Resolve an instance from the service provider after calling <c>AddSendr</c>.
/// </summary>
public interface IStreamSender
{
    /// <summary>
    /// Dispatches a stream request to its handler and returns the produced asynchronous sequence.
    /// The sequence is lazy: handling begins when enumeration starts.
    /// </summary>
    /// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
    /// <param name="request">The stream request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while enumerating the sequence.</param>
    /// <returns>An asynchronous sequence of responses produced for the request.</returns>
    IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}

internal sealed class Sender(IServiceProvider sp, HandlerRegistry registry) : ISender, IStreamSender
{
    public Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
        => ((RequestHandler)
                registry.GetOrCreate(request.GetType()))
            .HandleAsync(request, sp, cancellationToken);

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => ((RequestHandler<TResponse>)
                registry.GetOrCreate(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);

    public IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => ((StreamRequestHandler<TResponse>)
                registry.GetOrCreateStream(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);
}

internal enum HandlerKind
{
    Request,
    RequestResponse,
    Stream,
}

/// <summary>
/// Per-container cache that maps a (request type, kind) to the dispatch wrapper that resolves
/// and invokes the registered handler. Registered as a singleton by <c>AddSendr</c>.
/// </summary>
internal sealed class HandlerRegistry
{
    private readonly ConcurrentDictionary<(Type Request, HandlerKind Kind), RequestHandlerBase> _cache = new();

    public RequestHandlerBase GetOrCreate(Type requestType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.Request),
            static key => Create(typeof(RequestHandlerImpl<>)
                .MakeGenericType(key.Request)));

    public RequestHandlerBase GetOrCreate(Type requestType, Type responseType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.RequestResponse),
            key => Create(typeof(RequestHandlerImpl<,>)
                .MakeGenericType(key.Request, responseType)));

    public RequestHandlerBase GetOrCreateStream(Type requestType, Type responseType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.Stream),
            key => Create(typeof(StreamRequestHandlerImpl<,>)
                .MakeGenericType(key.Request, responseType)));

    private static RequestHandlerBase Create(Type handlerType)
    {
        var ctor = handlerType.GetConstructor(Type.EmptyTypes)!;
        var lambda = Expression.Lambda<Func<RequestHandlerBase>>(
            Expression.Convert(Expression.New(ctor), typeof(RequestHandlerBase)));

        return lambda.Compile()();
    }
}
