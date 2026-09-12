namespace Davish.Sendr;

/// <summary>
/// Adds cross-cutting behaviour (such as logging or caching) around a query handler. A single
/// decorator instance can be applied to any query/response pair; the types are parameters of
/// <see cref="HandleAsync{TQuery, TResponse}"/> rather than of the interface.
/// </summary>
public interface IQueryDecorator
{
    /// <summary>
    /// Wraps the handling of <paramref name="query"/>, calling <paramref name="next"/> to run
    /// the inner pipeline step.
    /// </summary>
    /// <typeparam name="TQuery">The type of query being handled.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
    /// <param name="query">The query being handled.</param>
    /// <param name="next">Invokes the inner decorator or the handler.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the query.</returns>
    Task<TResponse> HandleAsync<TQuery, TResponse>(
        TQuery query,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
        where TQuery : IQuery<TResponse>;
}
