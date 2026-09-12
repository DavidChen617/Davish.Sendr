namespace Davish.Sendr;

/// <summary>
/// Handles a query and returns a response of type <typeparamref name="TResponse"/>.
/// Independent of <see cref="IRequestHandler{TRequest, TResponse}"/> — dispatched via
/// <c>ISender.SendAsync{TResponse}(IQuery{TResponse}, CancellationToken)</c>.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// Handles the specified <paramref name="query"/> and returns its response.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the query.</returns>
    Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
