namespace Davish.Sendr.Implements;

/// <summary>
/// Bridges a non-generic <see cref="ICommandDecorator.WithResponse"/> back into the
/// <see cref="ICommandHandler{TCommand, TResponse}"/> chain so the decorator pipeline can be
/// composed at registration time.
/// </summary>
internal sealed class CommandDecoratorHandler<TCommand, TResponse>(
    ICommandDecorator.WithResponse decorator,
    ICommandHandler<TCommand, TResponse> inner)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               command,
               () => inner.HandleAsync(command, cancellationToken),
               cancellationToken);
}

/// <summary>
/// Bridges a non-generic <see cref="ICommandDecorator"/> back into the
/// <see cref="ICommandHandler{TCommand}"/> chain so the decorator pipeline can be composed at
/// registration time.
/// </summary>
internal sealed class CommandDecoratorHandler<TCommand>(
    ICommandDecorator decorator,
    ICommandHandler<TCommand> inner)
    : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public Task HandleAsync(TCommand command, CancellationToken cancellationToken)
        => decorator.HandleAsync(
               command,
               () => inner.HandleAsync(command, cancellationToken),
               cancellationToken);
}
