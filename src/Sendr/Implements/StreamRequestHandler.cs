using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr.Implements;

internal abstract class StreamRequestHandler<TResponse> : RequestHandlerBase
{
    public abstract IAsyncEnumerable<TResponse> HandleAsync(IStreamRequest<TResponse> request, IServiceProvider sp,
        CancellationToken cancellationToken);
}

internal sealed class StreamRequestHandlerImpl<TRequest, TResponse> : StreamRequestHandler<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public override IAsyncEnumerable<TResponse> HandleAsync(
        IStreamRequest<TResponse> request,
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        var handler = sp.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw HandlerResolutionException.NoHandler("AddStreamRequestHandler", typeof(TRequest), typeof(TResponse));
        return handler.HandleAsync((TRequest)request, cancellationToken);
    }
}
