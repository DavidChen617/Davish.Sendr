namespace Davish.Sendr;

/// <summary>
/// Handles a request that does not produce a response value.
/// </summary>
/// <typeparam name="TRequest">The type of request to handle.</typeparam>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    /// <summary>
    /// Handles the specified <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    Task HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles a request and produces a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The type of request to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the specified <paramref name="request"/> and returns its response.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the request.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Invokes the next step in a request pipeline (the inner decorator or the handler itself).
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the next step.</typeparam>
/// <returns>A task that resolves to the response produced by the next step.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Invokes the next step in a request pipeline (the inner decorator or the handler itself)
/// for a request that does not produce a response value.
/// </summary>
/// <returns>A task that completes when the next step has run.</returns>
public delegate Task RequestHandlerDelegate();

/// <summary>
/// Adds cross-cutting behaviour (such as logging, validation, or transactions) around a
/// request handler that does not produce a response value. A single decorator instance can
/// be applied to any request type; the request type is a parameter of <see cref="HandleAsync"/>
/// rather than of the interface.
/// </summary>
public interface IRequestDecorator
{
    /// <summary>
    /// Wraps the handling of <paramref name="request"/>, calling <paramref name="next"/> to run
    /// the inner pipeline step.
    /// </summary>
    /// <typeparam name="TRequest">The type of request being handled.</typeparam>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">Invokes the inner decorator or the handler.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    Task HandleAsync<TRequest>(
        TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Adds cross-cutting behaviour (such as logging, validation, or transactions) around a
    /// request handler that produces a response. A single decorator instance can be applied to
    /// any request/response pair; the types are parameters of <see cref="HandleAsync"/> rather
    /// than of the interface.
    /// </summary>
    public interface WithResponse
    {
        /// <summary>
        /// Wraps the handling of <paramref name="request"/>, calling <paramref name="next"/> to run
        /// the inner pipeline step.
        /// </summary>
        /// <typeparam name="TRequest">The type of request being handled.</typeparam>
        /// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
        /// <param name="request">The request being handled.</param>
        /// <param name="next">Invokes the inner decorator or the handler.</param>
        /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
        /// <returns>A task that resolves to the response produced for the request.</returns>
        Task<TResponse> HandleAsync<TRequest, TResponse>(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>;
    }
}

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
        TRequest request, StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TResponse>;
}

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
    IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
