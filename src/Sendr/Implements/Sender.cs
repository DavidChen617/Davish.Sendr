namespace Davish.Sendr.Implements;

internal sealed class Sender(IServiceProvider sp, HandlerRegistry registry) : ISender
{
    public Task SendAsync(IRequest request, CancellationToken cancellationToken)
        => ((RequestHandler)
                registry.GetOrCreate(request.GetType()))
            .HandleAsync(request, sp, cancellationToken);

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken)
        => ((RequestHandler<TResponse>)
                registry.GetOrCreate(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);

    public IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken)
        => ((StreamRequestHandler<TResponse>)
                registry.GetOrCreateStream(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);

    public Task SendAsync(ICommand command, CancellationToken cancellationToken)
        => ((CommandHandler)
                registry.GetOrCreateCommand(command.GetType()))
            .HandleAsync(command, sp, cancellationToken);

    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command,
        CancellationToken cancellationToken)
        => ((CommandHandler<TResponse>)
                registry.GetOrCreateCommand(command.GetType(), typeof(TResponse)))
            .HandleAsync(command, sp, cancellationToken);

    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query,
        CancellationToken cancellationToken)
        => ((QueryHandler<TResponse>)
                registry.GetOrCreateQuery(query.GetType(), typeof(TResponse)))
            .HandleAsync(query, sp, cancellationToken);
}
