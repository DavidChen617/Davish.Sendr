using System.Collections.Concurrent;
using System.Linq.Expressions;
using Davish.Sendr;

namespace Davish.Sendr.Implements;

internal enum HandlerKind
{
    Request,
    RequestResponse,
    Stream,
    Command,
    CommandResponse,
    Query,
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

    public RequestHandlerBase GetOrCreateCommand(Type commandType) =>
        _cache.GetOrAdd(
            (commandType, HandlerKind.Command),
            static key => Create(typeof(CommandHandlerImpl<>)
                .MakeGenericType(key.Request)));

    public RequestHandlerBase GetOrCreateCommand(Type commandType, Type responseType) =>
        _cache.GetOrAdd(
            (commandType, HandlerKind.CommandResponse),
            key => Create(typeof(CommandHandlerImpl<,>)
                .MakeGenericType(key.Request, responseType)));

    public RequestHandlerBase GetOrCreateQuery(Type queryType, Type responseType) =>
        _cache.GetOrAdd(
            (queryType, HandlerKind.Query),
            key => Create(typeof(QueryHandlerImpl<,>)
                .MakeGenericType(key.Request, responseType)));

    private static RequestHandlerBase Create(Type handlerType)
    {
        var ctor = handlerType.GetConstructor(Type.EmptyTypes)!;
        var lambda = Expression.Lambda<Func<RequestHandlerBase>>(
            Expression.Convert(Expression.New(ctor), typeof(RequestHandlerBase)));

        return lambda.Compile()();
    }
}
