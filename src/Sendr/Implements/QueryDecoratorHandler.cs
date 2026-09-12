namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="IQueryDecorator"/> back into the
/// <see cref="IQueryHandler{TQuery, TResponse}"/> chain so the decorator pipeline can be
/// composed at registration time.
/// </summary>
internal sealed class QueryDecoratorHandler<TQuery, TResponse>(
    IQueryDecorator decorator,
    IQueryHandler<TQuery, TResponse> inner)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               query,
               () => inner.HandleAsync(query, cancellationToken),
               cancellationToken);
}
