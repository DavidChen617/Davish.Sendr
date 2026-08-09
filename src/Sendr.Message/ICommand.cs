namespace Davish.Sendr;

/// <summary>
/// Marker interface for a command: a request that changes state and does not
/// return a response value. Handled by an <see cref="ICommandHandler{TRequest}"/>.
/// </summary>
public interface ICommand : IRequest;

/// <summary>
/// Marker interface for a command that changes state and returns a response
/// of type <typeparamref name="TResponse"/> (for example, a generated identifier).
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;
