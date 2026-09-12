namespace Davish.Sendr;

/// <summary>
/// Marker interface for a query: a request that reads state without modifying
/// it and returns a response of type <typeparamref name="TResponse"/>. A query is not an
/// <see cref="IRequest{TResponse}"/> — it dispatches through its own
/// <c>ISender.SendAsync{TResponse}(IQuery{TResponse}, CancellationToken)</c> overload.
/// </summary>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IQuery<out TResponse>;
