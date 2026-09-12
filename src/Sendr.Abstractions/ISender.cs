namespace Davish.Sendr;

/// <summary>
/// Dispatches stream requests to their registered handlers, running any configured decorator
/// pipeline. Resolve an instance from the service provider after calling <c>AddSendr</c>.
/// </summary>
public interface IStreamSender
{
    /// <summary>
    /// Dispatches a stream request to its handler and returns the produced asynchronous sequence.
    /// The sequence is lazy: handling begins when enumeration starts.
    /// </summary>
    /// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
    /// <param name="request">The stream request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while enumerating the sequence.</param>
    /// <returns>An asynchronous sequence of responses produced for the request.</returns>
    IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken);
}

/// <summary>
/// Dispatches requests to their registered handlers, running any configured decorator pipeline.
/// Extends <see cref="IStreamSender"/>, so an <see cref="ISender"/> can also dispatch stream
/// requests without resolving a separate service. Resolve an instance from the service provider
/// after calling <c>AddSendr</c>.
/// </summary>
public interface ISender : IStreamSender
{
    /// <summary>
    /// Dispatches a request that does not produce a response value to its handler.
    /// </summary>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    Task SendAsync(IRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches a request to its handler and returns the produced response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the request.</typeparam>
    /// <param name="request">The request to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the request.</returns>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches a command that does not produce a response value to its handler.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the command has been handled.</returns>
    Task SendAsync(ICommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches a command to its handler and returns the produced response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the command.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the command.</returns>
    Task<TResponse> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken cancellationToken);

    /// <summary>
    /// Dispatches a query to its handler and returns the produced response.
    /// </summary>
    /// <typeparam name="TResponse">The type of response expected from the query.</typeparam>
    /// <param name="query">The query to dispatch.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the query.</returns>
    Task<TResponse> SendAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken cancellationToken);
}
