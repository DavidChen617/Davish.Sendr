using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Sendr services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class Dependency
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="ISender"/> service so requests can be dispatched.
        /// Call this once during application startup, then register handlers with
        /// <see cref="AddRequestHandler{TRequest, TResponse, THandler}"/>.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddSendr()
        {
            return services.AddScoped<ISender, Sender>();
        }

        /// <summary>
        /// Registers a handler for the request/response pair, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="RequestHandlerOptions{TRequest, TResponse}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TRequest">The request type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="TResponse">The response type produced for the request.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(
            Action<RequestHandlerOptions<TRequest, TResponse>>? configure = null)
            where TRequest : IRequest<TResponse>
            where THandler : class, IRequestHandler<TRequest, TResponse>
        {
            HandlersCache.GetOrCreate(typeof(TRequest), typeof(TResponse));

            services.AddTransient<THandler>();

            var options = new RequestHandlerOptions<TRequest, TResponse>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IRequestHandler<TRequest, TResponse>>(pv =>
            {
                IRequestHandler<TRequest, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (IRequestDecorator.WithResponse)pv.GetRequiredService(decoratorType);
                    handler = new DecoratorHandlerImpl<TRequest, TResponse>(decorator, handler);
                }

                return handler;
            });

            return services;
        }

        /// <summary>
        /// Registers a handler for the request, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="RequestHandlerOptions{TRequest}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TRequest">The request type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddRequestHandler<TRequest, THandler>(
            Action<RequestHandlerOptions<TRequest>>? configure = null)
            where TRequest : IRequest
            where THandler : class, IRequestHandler<TRequest>
        {
            HandlersCache.GetOrCreate(typeof(TRequest));

            services.AddTransient<THandler>();

            var options = new RequestHandlerOptions<TRequest>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IRequestHandler<TRequest>>(pv =>
            {
                IRequestHandler<TRequest> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (IRequestDecorator)pv.GetRequiredService(decoratorType);
                    handler = new DecoratorHandlerImpl<TRequest>(decorator, handler);
                }

                return handler;
            });

            return services;
        }
    }
}

/// <summary>
/// Configures how a request/response handler is registered, most notably the decorator
/// pipeline applied around it.
/// </summary>
/// <typeparam name="TRequest">The request type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the request.</typeparam>
public sealed class RequestHandlerOptions<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public DecoratorBuilder<TRequest, TResponse> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="RequestHandlerOptions{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The request type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the request.</typeparam>
public sealed class DecoratorBuilder<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly RequestHandlerOptions<TRequest, TResponse> _options;

    internal DecoratorBuilder(RequestHandlerOptions<TRequest, TResponse> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="IRequestDecorator.WithResponse"/>.
    /// </typeparam>
    /// <returns>The same <see cref="DecoratorBuilder{TRequest, TResponse}"/> so that calls can be chained.</returns>
    public DecoratorBuilder<TRequest, TResponse> With<TDecorator>()
        where TDecorator : IRequestDecorator.WithResponse
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}

/// <summary>
/// Configures how a request handler is registered, most notably the decorator pipeline
/// applied around it.
/// </summary>
/// <typeparam name="TRequest">The request type being handled.</typeparam>
public sealed class RequestHandlerOptions<TRequest>
    where TRequest : IRequest
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public DecoratorBuilder<TRequest> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="RequestHandlerOptions{TRequest}"/>.
/// </summary>
/// <typeparam name="TRequest">The request type being handled.</typeparam>
public sealed class DecoratorBuilder<TRequest>
    where TRequest : IRequest
{
    private readonly RequestHandlerOptions<TRequest> _options;

    internal DecoratorBuilder(RequestHandlerOptions<TRequest> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="IRequestDecorator"/>.
    /// </typeparam>
    /// <returns>The same <see cref="DecoratorBuilder{TRequest}"/> so that calls can be chained.</returns>
    public DecoratorBuilder<TRequest> With<TDecorator>()
        where TDecorator : IRequestDecorator
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}
