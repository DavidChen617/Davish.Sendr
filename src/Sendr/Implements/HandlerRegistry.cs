using System.Collections.Concurrent;
using System.Linq.Expressions;

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
    // Response is part of the key (not just captured in the creator closure) because a single
    // request/command/query type can implement IRequest<TResponse>/ICommand<TResponse>/
    // IQuery<TResponse> for more than one TResponse. Keying on request type alone would let
    // whichever TResponse's call won the GetOrAdd race permanently shadow every other TResponse
    // for that request type's lifetime in this container.
    private readonly ConcurrentDictionary<(Type Request, HandlerKind Kind, Type? Response), RequestHandlerBase> _cache = new();

    public RequestHandlerBase GetOrCreate(Type requestType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.Request, null),
            static key => Create(typeof(RequestHandlerImpl<>)
                .MakeGenericType(key.Request)));

    public RequestHandlerBase GetOrCreate(Type requestType, Type responseType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.RequestResponse, responseType),
            static key => Create(typeof(RequestHandlerImpl<,>)
                .MakeGenericType(key.Request, key.Response!)));

    public RequestHandlerBase GetOrCreateStream(Type requestType, Type responseType) =>
        _cache.GetOrAdd(
            (requestType, HandlerKind.Stream, responseType),
            static key => Create(typeof(StreamRequestHandlerImpl<,>)
                .MakeGenericType(key.Request, key.Response!)));

    public RequestHandlerBase GetOrCreateCommand(Type commandType) =>
        _cache.GetOrAdd(
            (commandType, HandlerKind.Command, null),
            static key => Create(typeof(CommandHandlerImpl<>)
                .MakeGenericType(key.Request)));

    public RequestHandlerBase GetOrCreateCommand(Type commandType, Type responseType) =>
        _cache.GetOrAdd(
            (commandType, HandlerKind.CommandResponse, responseType),
            static key => Create(typeof(CommandHandlerImpl<,>)
                .MakeGenericType(key.Request, key.Response!)));

    public RequestHandlerBase GetOrCreateQuery(Type queryType, Type responseType) =>
        _cache.GetOrAdd(
            (queryType, HandlerKind.Query, responseType),
            static key => Create(typeof(QueryHandlerImpl<,>)
                .MakeGenericType(key.Request, key.Response!)));

    private static RequestHandlerBase Create(Type handlerType)
    {
        var ctor = handlerType.GetConstructor(Type.EmptyTypes)!;
        var lambda = Expression.Lambda<Func<RequestHandlerBase>>(
            Expression.Convert(Expression.New(ctor), typeof(RequestHandlerBase)));

        return lambda.Compile()();
    }
}
