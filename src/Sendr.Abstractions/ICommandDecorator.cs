namespace Davish.Sendr;

/// <summary>
/// Adds cross-cutting behaviour (such as logging, validation, or transactions) around a
/// command handler that does not produce a response value. A single decorator instance can
/// be applied to any command type; the command type is a parameter of <see cref="HandleAsync"/>
/// rather than of the interface.
/// </summary>
public interface ICommandDecorator
{
    /// <summary>
    /// Wraps the handling of <paramref name="command"/>, calling <paramref name="next"/> to run
    /// the inner pipeline step.
    /// </summary>
    /// <typeparam name="TCommand">The type of command being handled.</typeparam>
    /// <param name="command">The command being handled.</param>
    /// <param name="next">Invokes the inner decorator or the handler.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the command has been handled.</returns>
    Task HandleAsync<TCommand>(
        TCommand command,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand;

    /// <summary>
    /// Adds cross-cutting behaviour (such as logging, validation, or transactions) around a
    /// command handler that produces a response. A single decorator instance can be applied to
    /// any command/response pair; the types are parameters of <see cref="HandleAsync"/> rather
    /// than of the interface.
    /// </summary>
    public interface WithResponse
    {
        /// <summary>
        /// Wraps the handling of <paramref name="command"/>, calling <paramref name="next"/> to run
        /// the inner pipeline step.
        /// </summary>
        /// <typeparam name="TCommand">The type of command being handled.</typeparam>
        /// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
        /// <param name="command">The command being handled.</param>
        /// <param name="next">Invokes the inner decorator or the handler.</param>
        /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
        /// <returns>A task that resolves to the response produced for the command.</returns>
        Task<TResponse> HandleAsync<TCommand, TResponse>(
            TCommand command,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken
        )
            where TCommand : ICommand<TResponse>;
    }
}
