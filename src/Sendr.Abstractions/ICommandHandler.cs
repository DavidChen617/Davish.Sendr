namespace Davish.Sendr;

/// <summary>
/// Handles a command that does not produce a response value. Independent of
/// <see cref="IRequestHandler{TRequest}"/> — dispatched via
/// <c>ISender.SendAsync(ICommand, CancellationToken)</c>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>
    /// Handles the specified <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that completes when the command has been handled.</returns>
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command and returns a response of type <typeparamref name="TResponse"/>.
/// Independent of <see cref="IRequestHandler{TRequest, TResponse}"/> — dispatched via
/// <c>ISender.SendAsync{TResponse}(ICommand{TResponse}, CancellationToken)</c>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// Handles the specified <paramref name="command"/> and returns its response.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the operation.</param>
    /// <returns>A task that resolves to the response produced for the command.</returns>
    Task<TResponse> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
