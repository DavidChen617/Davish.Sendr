namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="IStreamRequestDecorator"/> back into the
/// <see cref="IStreamRequestHandler{TRequest, TResponse}"/> chain so the decorator pipeline can
/// be composed at registration time.
/// </summary>
internal sealed class StreamDecoratorHandler<TRequest, TResponse>(
    IStreamRequestDecorator decorator,
    IStreamRequestHandler<TRequest, TResponse> inner)
    : IStreamRequestHandler<TRequest, TResponse>, IDisposable, IAsyncDisposable
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);

    // See DecoratorHandler<TRequest, TResponse> for why this forwards disposal to inner.
    public void Dispose()
    {
        if (inner is IDisposable disposable)
            disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        switch (inner)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
