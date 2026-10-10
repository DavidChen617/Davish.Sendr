namespace Davish.Sendr.Implements;

internal sealed class Sender(IServiceProvider sp, HandlerRegistry registry) : ISender
{
    public Task SendAsync(IRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!SendrActivitySource.IsEnabled)
            return ((RequestHandler)
                registry.GetOrCreate(request.GetType()))
            .HandleAsync(request, sp, cancellationToken);

        return SendrActivitySource.Invoke("request", request.GetType(),
            (registry, sp, request, cancellationToken),
            static s => ((RequestHandler)s.registry.GetOrCreate(s.request.GetType()))
                .HandleAsync(s.request, s.sp, s.cancellationToken));
    }

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (!SendrActivitySource.IsEnabled)
            return ((RequestHandler<TResponse>)
                registry.GetOrCreate(request.GetType(), typeof(TResponse)))
            .HandleAsync(request, sp, cancellationToken);

        return SendrActivitySource.Invoke("request", request.GetType(),
            (registry, sp, request, cancellationToken),
            static s => ((RequestHandler<TResponse>)s.registry.GetOrCreate(s.request.GetType(), typeof(TResponse)))
                .HandleAsync(s.request, s.sp, s.cancellationToken));
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

        if (!SendrActivitySource.IsEnabled)
            return ((CommandHandler)
                registry.GetOrCreateCommand(command.GetType()))
            .HandleAsync(command, sp, cancellationToken);

        return SendrActivitySource.Invoke("command", command.GetType(),
            (registry, sp, command, cancellationToken),
            static s => ((CommandHandler)s.registry.GetOrCreateCommand(s.command.GetType()))
                .HandleAsync(s.command, s.sp, s.cancellationToken));
    }

    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command,
        CancellationToken cancellationToken)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (!SendrActivitySource.IsEnabled)
            return ((CommandHandler<TResponse>)
                registry.GetOrCreateCommand(command.GetType(), typeof(TResponse)))
            .HandleAsync(command, sp, cancellationToken);

        return SendrActivitySource.Invoke("command", command.GetType(),
            (registry, sp, command, cancellationToken),
            static s => ((CommandHandler<TResponse>)s.registry.GetOrCreateCommand(s.command.GetType(), typeof(TResponse)))
                .HandleAsync(s.command, s.sp, s.cancellationToken));
    }

    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query,
        CancellationToken cancellationToken)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        if (!SendrActivitySource.IsEnabled)
            return ((QueryHandler<TResponse>)
                registry.GetOrCreateQuery(query.GetType(), typeof(TResponse)))
            .HandleAsync(query, sp, cancellationToken);

        return SendrActivitySource.Invoke("query", query.GetType(),
            (registry, sp, query, cancellationToken),
            static s => ((QueryHandler<TResponse>)s.registry.GetOrCreateQuery(s.query.GetType(), typeof(TResponse)))
                .HandleAsync(s.query, s.sp, s.cancellationToken));
    }
}
