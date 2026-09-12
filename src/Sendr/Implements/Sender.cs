namespace Davish.Sendr.Implements;

internal sealed class Sender(IServiceProvider sp, HandlerRegistry registry) : ISender
{
    public Task SendAsync(IRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return ((RequestHandler)
                registry.GetOrCreate(request.GetType()))
            .HandleAsync(request, sp, cancellationToken);
    }

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return ((RequestHandler<TResponse>)
                registry.GetOrCreate(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> SendStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        return ((StreamRequestHandler<TResponse>)
                registry.GetOrCreateStream(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);
    }

    public Task SendAsync(ICommand command, CancellationToken cancellationToken)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        return ((CommandHandler)
                registry.GetOrCreateCommand(command.GetType()))
            .HandleAsync(command, sp, cancellationToken);
    }

    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        return ((CommandHandler<TResponse>)
                registry.GetOrCreateCommand(command.GetType(), typeof(TResponse)))
            .HandleAsync(command, sp, cancellationToken);
    }

    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query,
        CancellationToken cancellationToken)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        return ((QueryHandler<TResponse>)
                registry.GetOrCreateQuery(query.GetType(), typeof(TResponse)))
            .HandleAsync(query, sp, cancellationToken);
    }
}
