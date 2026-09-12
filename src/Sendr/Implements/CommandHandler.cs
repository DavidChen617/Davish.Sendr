using Microsoft.Extensions.DependencyInjection;

namespace Davish.Sendr.Implements;

internal abstract class CommandHandler : RequestHandlerBase
{
    public abstract Task HandleAsync(ICommand command, IServiceProvider sp,
        CancellationToken cancellationToken);
}

internal abstract class CommandHandler<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> HandleAsync(
        ICommand<TResponse> command,
        IServiceProvider sp,
        CancellationToken cancellationToken);
}

internal sealed class CommandHandlerImpl<TCommand> : CommandHandler
    where TCommand : ICommand
{
    public override Task HandleAsync(
        ICommand command,
        IServiceProvider sp,
        CancellationToken cancellationToken)
        => sp
            .GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync((TCommand)command, cancellationToken);
}

internal sealed class CommandHandlerImpl<TCommand, TResponse> : CommandHandler<TResponse>
    where TCommand : ICommand<TResponse>
{
    public override Task<TResponse> HandleAsync(
        ICommand<TResponse> command,
        IServiceProvider sp,
        CancellationToken cancellationToken)
        => sp
            .GetRequiredService<ICommandHandler<TCommand, TResponse>>()
            .HandleAsync((TCommand)command, cancellationToken);
}
