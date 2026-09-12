namespace Davish.Sendr;

/// <summary>
/// Marker interface for a command: a request that changes state and does not
/// return a response value. Handled by an <see cref="ICommandHandler{TRequest}"/>.
/// A command is not an <see cref="IRequest"/> — it dispatches through its own
/// <c>ISender.SendAsync(ICommand, CancellationToken)</c> overload.
/// </summary>
public interface ICommand;

/// <summary>
/// Marker interface for a command that changes state and returns a response
/// of type <typeparamref name="TResponse"/> (for example, a generated identifier).
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface ICommand<out TResponse> : ICommand;
