namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="IStreamRequestDecorator"/> back into the
/// <see cref="IStreamRequestHandler{TRequest, TResponse}"/> chain so the decorator pipeline can
/// be composed at registration time.
/// </summary>
internal sealed class StreamDecoratorHandler<TRequest, TResponse>(
    IStreamRequestDecorator decorator,
    IStreamRequestHandler<TRequest, TResponse> inner)
    : IStreamRequestHandler<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);
}
