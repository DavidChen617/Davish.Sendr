namespace Davish.Sendr;

public interface IRequest;

public interface IRequest<out TResponse> : IRequest;
