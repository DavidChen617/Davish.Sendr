using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Davish.Sendr;

public interface ISender
{
    Task SendAsync(IRequest request, CancellationToken cancellationToken = default);

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
