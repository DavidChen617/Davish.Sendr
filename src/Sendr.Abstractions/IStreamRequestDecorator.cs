namespace Davish.Sendr;

/// <summary>
/// Invokes the next step in a stream request pipeline (the inner decorator or the handler itself).
/// </summary>
/// <typeparam name="TResponse">The type of each item produced by the next step.</typeparam>
/// <returns>The asynchronous sequence produced by the next step.</returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Adds cross-cutting behaviour around a stream request handler. A single decorator instance can
/// be applied to any request/response pair; the types are parameters of <see cref="HandleAsync"/>
/// rather than of the interface.
/// </summary>
public interface IStreamRequestDecorator
{
    /// <summary>
    /// Wraps the handling of <paramref name="request"/>, calling <paramref name="next"/> to run
    /// the inner pipeline step. Implementations that use <c>yield</c> should wrap the enumeration
    /// in <c>try/finally</c> and forward the token via <c>[EnumeratorCancellation]</c>.
    /// </summary>
    /// <typeparam name="TRequest">The type of stream request being handled.</typeparam>
    /// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">Invokes the inner decorator or the handler.</param>
    /// <param name="cancellationToken">A token to observe while enumerating the sequence.</param>
    /// <returns>The asynchronous sequence produced for the request.</returns>
    IAsyncEnumerable<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
        where TRequest : IStreamRequest<TResponse>;
}
