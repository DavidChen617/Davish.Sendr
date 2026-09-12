namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="ICommandDecorator.WithResponse"/> back into the
/// <see cref="ICommandHandler{TCommand, TResponse}"/> chain so the decorator pipeline can be
/// composed at registration time.
/// </summary>
internal sealed class CommandDecoratorHandler<TCommand, TResponse>(
    ICommandDecorator.WithResponse decorator,
    ICommandHandler<TCommand, TResponse> inner)
    : ICommandHandler<TCommand, TResponse>, IDisposable, IAsyncDisposable
    where TCommand : ICommand<TResponse>
{
    public Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               command,
               () => inner.HandleAsync(command, cancellationToken),
               cancellationToken);

    // See DecoratorHandler<TRequest, TResponse> for why this forwards disposal to inner.
    public void Dispose() => HandlerDisposal.DisposeSync(inner);

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
/// Bridges a non-generic <see cref="ICommandDecorator"/> back into the
/// <see cref="ICommandHandler{TCommand}"/> chain so the decorator pipeline can be composed at
/// registration time.
/// </summary>
internal sealed class CommandDecoratorHandler<TCommand>(
    ICommandDecorator decorator,
    ICommandHandler<TCommand> inner)
    : ICommandHandler<TCommand>, IDisposable, IAsyncDisposable
    where TCommand : ICommand
{
    public Task HandleAsync(TCommand command, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               command,
               () => inner.HandleAsync(command, cancellationToken),
               cancellationToken);

    // See DecoratorHandler<TRequest, TResponse> for why this forwards disposal to inner.
    public void Dispose() => HandlerDisposal.DisposeSync(inner);

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
