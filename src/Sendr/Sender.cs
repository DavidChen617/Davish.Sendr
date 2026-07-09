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

internal sealed class Sender(IServiceProvider sp) : ISender
{
    public Task SendAsync(IRequest request, CancellationToken cancellationToken = default)
    {
        var handler = (RequestHandler)HandlersCache
            .GetOrCreate(request.GetType());
        return handler.HandleAsync(request, sp, cancellationToken);
    }

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        var handler = (RequestHandler<TResponse>)HandlersCache
            .GetOrCreate(request.GetType(), typeof(TResponse));

        return handler.HandleAsync(request, sp, cancellationToken);
    }
}

internal static class HandlersCache
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> Handlers = new();

    public static RequestHandlerBase GetOrCreate(Type requestType)
    {
        return Handlers
            .GetOrAdd(requestType, static t =>
            {
                var requestType =
                    typeof(RequestHandlerImpl<>)
                        .MakeGenericType(t);

                var ctor = requestType.GetConstructor(Type.EmptyTypes)!;
                var lambda = Expression.Lambda<Func<RequestHandlerBase>>(
                    Expression.Convert(Expression.New(ctor), typeof(RequestHandlerBase)));

                return lambda.Compile()();
            });
    }

    public static RequestHandlerBase GetOrCreate(Type requestType, Type responseType) =>
        Handlers
            .GetOrAdd(requestType, t =>
            {
                var handlerType =
                    typeof(RequestHandlerImpl<,>).MakeGenericType(t, responseType);
                var ctor = handlerType.GetConstructor(Type.EmptyTypes)!;
                var lambda = Expression.Lambda<Func<RequestHandlerBase>>(
                    Expression.Convert(Expression.New(ctor), typeof(RequestHandlerBase)));

                return lambda.Compile()();
            });
}
