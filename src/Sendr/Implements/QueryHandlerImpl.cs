using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr.Implements;

internal abstract class QueryHandler<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> HandleAsync(
        IQuery<TResponse> query,
        IServiceProvider sp,
        CancellationToken cancellationToken);
}

internal sealed class QueryHandlerImpl<TQuery, TResponse> : QueryHandler<TResponse>
    where TQuery : IQuery<TResponse>
{
    public override Task<TResponse> HandleAsync(
        IQuery<TResponse> query,
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        var handler = sp.GetService<IQueryHandler<TQuery, TResponse>>()
            ?? throw HandlerResolutionException.NoHandler("AddQueryHandler", typeof(TQuery), typeof(TResponse));
        return handler.HandleAsync((TQuery)query, cancellationToken);
    }
}
