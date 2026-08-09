namespace Davish.Sendr;

/// <summary>
/// Handles a command that does not produce a response value.
/// </summary>
/// <typeparam name="TRequest">The type of command to handle.</typeparam>
public interface ICommandHandler<in TRequest> : IRequestHandler<TRequest>
where TRequest : ICommand;

/// <summary>
/// Handles a command and returns a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The type of command to handle.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface ICommandHandler<in TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
where TRequest : ICommand<TResponse>;
