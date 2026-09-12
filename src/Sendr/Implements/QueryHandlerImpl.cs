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
        => sp
            .GetRequiredService<IQueryHandler<TQuery, TResponse>>()
            .HandleAsync((TQuery)query, cancellationToken);
}
