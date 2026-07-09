using Davish.Sendr;

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
        /// decorator pipeline.
        /// </summary>
        /// <typeparam name="TRequest">The request type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="TResponse">The response type produced for the request.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <see cref="RequestHandlerOptions.UseDecorators"/>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the handler is resolved (not at registration time) if any configured decorator
        /// type does not implement <see cref="IRequestDecorator{TRequest, TResponse}"/> for this
        /// request/response pair.
        /// </exception>
        public IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(Action<RequestHandlerOptions>? configure = null)
            where TRequest : IRequest<TResponse>
            where THandler : class, IRequestHandler<TRequest, TResponse>
        {
            HandlersCache.GetOrCreate(typeof(TRequest), typeof(TResponse));

            services.AddTransient<THandler>();

            var options = new RequestHandlerOptions();
            configure?.Invoke(options);

            services.AddTransient<IRequestHandler<TRequest, TResponse>>(pv =>
            {
                IRequestHandler<TRequest, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var closed = decoratorType.MakeGenericType(typeof(TRequest), typeof(TResponse));

                    if (!typeof(IRequestDecorator<TRequest, TResponse>).IsAssignableFrom(closed))
                        throw new InvalidOperationException(
                            $"{decoratorType.Name} Not a valid decorator, must implement IRequestDecorator<TRequest, TResponse>.");

                    handler = (IRequestHandler<TRequest, TResponse>)ActivatorUtilities.CreateInstance(pv, closed, handler);
                }

                return handler;
            });

            return services;
        }
    }
}

/// <summary>
/// Configures how a request handler is registered, most notably the decorator pipeline
/// applied around it.
/// </summary>
public sealed class RequestHandlerOptions
{
    internal List<Type> Decorators { get; } = [];

    /// <summary>
    /// Adds decorators to wrap the handler. Each type must be an open generic definition
    /// (for example <c>typeof(LoggingHandler&lt;,&gt;)</c>) implementing
    /// <see cref="IRequestDecorator{TRequest, TResponse}"/>. Decorators are wrapped in the
    /// order supplied, so the last decorator supplied forms the outermost layer and executes first.
    /// </summary>
    /// <param name="decoratorTypeDefinitions">The open generic decorator type definitions to apply.</param>
    /// <returns>The same <see cref="RequestHandlerOptions"/> so that calls can be chained.</returns>
    public RequestHandlerOptions UseDecorators(params Type[] decoratorTypeDefinitions)
    {
        Decorators.AddRange(decoratorTypeDefinitions);
        return this;
    }
}
