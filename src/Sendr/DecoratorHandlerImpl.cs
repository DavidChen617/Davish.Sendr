namespace Davish.Sendr;

/// <summary>
/// Bridges a non-generic <see cref="IRequestDecorator.WithResponse"/> back into the
/// <see cref="IRequestHandler{TRequest, TResponse}"/> chain so the decorator pipeline can be
/// composed at registration time.
/// </summary>
internal sealed class DecoratorHandlerImpl<TRequest, TResponse>(
    IRequestDecorator.WithResponse decorator,
    IRequestHandler<TRequest, TResponse> inner)
    : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);
}

/// <summary>
/// Bridges a non-generic <see cref="IRequestDecorator"/> back into the
/// <see cref="IRequestHandler{TRequest}"/> chain so the decorator pipeline can be composed at
/// registration time.
/// </summary>
internal sealed class DecoratorHandlerImpl<TRequest>(
    IRequestDecorator decorator,
    IRequestHandler<TRequest> inner)
    : IRequestHandler<TRequest>
    where TRequest : IRequest
{
    public Task HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);
}

/// <summary>
/// Bridges a non-generic <see cref="IStreamRequestDecorator"/> back into the
/// <see cref="IStreamRequestHandler{TRequest, TResponse}"/> chain so the decorator pipeline can
/// be composed at registration time.
/// </summary>
internal sealed class StreamDecoratorHandlerImpl<TRequest, TResponse>(
    IStreamRequestDecorator decorator,
    IStreamRequestHandler<TRequest, TResponse> inner)
    : IStreamRequestHandler<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);
}
