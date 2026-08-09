namespace Davish.Sendr;

/// <summary>
/// Marker interface for a query: a request that reads state without modifying
/// it and returns a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
