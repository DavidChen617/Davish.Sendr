namespace Davish.Sendr;

/// <summary>
/// Handles a stream request and produces an asynchronous sequence of
/// <typeparamref name="TResponse"/> items.
/// </summary>
/// <typeparam name="TRequest">The type of stream request to handle.</typeparam>
/// <typeparam name="TResponse">The type of each item returned by the handler.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Handles the specified <paramref name="request"/> and returns its asynchronous sequence.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">A token to observe while enumerating the sequence.</param>
    /// <returns>An asynchronous sequence of responses produced for the request.</returns>
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
