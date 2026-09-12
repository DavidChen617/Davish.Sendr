namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="IRequestDecorator.WithResponse"/> back into the
/// <see cref="IRequestHandler{TRequest, TResponse}"/> chain so the decorator pipeline can be
/// composed at registration time.
/// </summary>
internal sealed class DecoratorHandler<TRequest, TResponse>(
    IRequestDecorator.WithResponse decorator,
    IRequestHandler<TRequest, TResponse> inner)
    : IRequestHandler<TRequest, TResponse>, IDisposable, IAsyncDisposable
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               request,
               () => inner.HandleAsync(request, cancellationToken),
               cancellationToken);

    // The container only ever tracks the literal object a registration's factory returns — when
    // a decorator wraps the handler, that's this wrapper, not the inner handler it was
    // constructed from via ActivatorUtilities (which the container never sees or tracks on its
    // own). Forwarding disposal here is what makes a disposable handler still get released when
    // it's decorated.
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

/// <summary>
/// Bridges a non-generic <see cref="IRequestDecorator"/> back into the
/// <see cref="IRequestHandler{TRequest}"/> chain so the decorator pipeline can be composed at
/// registration time.
/// </summary>
internal sealed class DecoratorHandler<TRequest>(
    IRequestDecorator decorator,
    IRequestHandler<TRequest> inner)
    : IRequestHandler<TRequest>, IDisposable, IAsyncDisposable
    where TRequest : IRequest
{
    public Task HandleAsync(TRequest request, CancellationToken cancellationToken)
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
