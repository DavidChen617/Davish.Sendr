using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr;

internal abstract class RequestHandlerBase;

internal abstract class RequestHandler : RequestHandlerBase
{
    public abstract Task HandleAsync(IRequest request, IServiceProvider sp,
        CancellationToken cancellationToken = default);
}

internal abstract class RequestHandler<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> HandleAsync(
        IRequest<TResponse> request,
        IServiceProvider sp,
        CancellationToken cancellationToken = default);
}

internal sealed class RequestHandlerImpl<TRequest> : RequestHandler
    where TRequest : IRequest
{
    public override Task HandleAsync(
        IRequest request,
        IServiceProvider sp,
        CancellationToken cancellationToken = default)
        => sp
            .GetRequiredService<IRequestHandler<TRequest>>()
            .HandleAsync((TRequest)request, cancellationToken);
}

internal sealed class RequestHandlerImpl<TRequest, TResponse> : RequestHandler<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> HandleAsync(
        IRequest<TResponse> request,
        IServiceProvider sp,
        CancellationToken cancellationToken = default)
        => sp
            .GetRequiredService<IRequestHandler<TRequest, TResponse>>()
            .HandleAsync((TRequest)request, cancellationToken);
}
