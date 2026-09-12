namespace Davish.Sendr;

/// <summary>
/// Marker interface for a request that can be dispatched through a sender.
/// A request with no response value is handled by an <see cref="IRequestHandler{TRequest}"/>.
/// </summary>
public interface IRequest;

/// <summary>
/// Marker interface for a request that produces a response of type <typeparamref name="TResponse"/>
/// when dispatched through a sender.
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IRequest<out TResponse> : IRequest;

/// <summary>
/// Marker interface for a request that produces an asynchronous stream of
/// <typeparamref name="TResponse"/> items when dispatched through a stream sender.
/// </summary>
/// <typeparam name="TResponse">The type of each item returned by the handler.</typeparam>
public interface IStreamRequest<out TResponse>;
