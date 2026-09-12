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

        return SendStreamCore(request, cancellationToken);
    }

    // Everything past the null check — resolving the registered handler and decorators via DI,
    // and running any of a decorator's own eager (non-yield) body before its `next()` call — is
    // deferred until enumeration actually starts, matching the documented "handling begins when
    // enumeration starts" contract. That requires this to be a real async-iterator method itself
    // (not a plain method that calls one and returns its result): only the compiler-generated
    // state machine behind `yield`/`await foreach` guarantees none of the body below runs before
    // the caller's first MoveNextAsync().
    private async IAsyncEnumerable<TResponse> SendStreamCore<TResponse>(
        IStreamRequest<TResponse> request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var handler = (StreamRequestHandler<TResponse>)
            registry.GetOrCreateStream(request.GetType(), typeof(TResponse));

        await foreach (var item in handler.HandleAsync(request, sp, cancellationToken).WithCancellation(cancellationToken))
            yield return item;
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
