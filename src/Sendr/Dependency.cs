using Davish.Sendr;
using Davish.Sendr.Implements;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
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
            services.AddSingleton<HandlerRegistry>();
            services.AddScoped<Sender>();
            services.AddScoped<ISender>(sp => sp.GetRequiredService<Sender>());
            services.AddScoped<IStreamSender>(sp => sp.GetRequiredService<Sender>());
            return services;
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
                    handler = new DecoratorHandler<TRequest, TResponse>(decorator, handler);
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
                    handler = new DecoratorHandler<TRequest>(decorator, handler);
                }

                return handler;
            });

            return services;
        }

        /// <summary>
        /// Registers a stream handler for the request, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="StreamRequestHandlerOptions{TRequest, TResponse}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TRequest">The stream request type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
        /// <typeparam name="THandler">The concrete stream handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddStreamRequestHandler<TRequest, TResponse, THandler>(
            Action<StreamRequestHandlerOptions<TRequest, TResponse>>? configure = null)
            where TRequest : IStreamRequest<TResponse>
            where THandler : class, IStreamRequestHandler<TRequest, TResponse>
        {
            services.AddTransient<THandler>();

            var options = new StreamRequestHandlerOptions<TRequest, TResponse>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IStreamRequestHandler<TRequest, TResponse>>(pv =>
            {
                IStreamRequestHandler<TRequest, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (IStreamRequestDecorator)pv.GetRequiredService(decoratorType);
                    handler = new StreamDecoratorHandler<TRequest, TResponse>(decorator, handler);
                }

                return handler;
            });

            return services;
        }

        /// <summary>
        /// Registers a handler for the command, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="CommandHandlerOptions{TCommand}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TCommand">The command type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddCommandHandler<TCommand, THandler>(
            Action<CommandHandlerOptions<TCommand>>? configure = null)
            where TCommand : ICommand
            where THandler : class, ICommandHandler<TCommand>
        {
            services.AddTransient<THandler>();

            var options = new CommandHandlerOptions<TCommand>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<ICommandHandler<TCommand>>(pv =>
            {
                ICommandHandler<TCommand> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (ICommandDecorator)pv.GetRequiredService(decoratorType);
                    handler = new CommandDecoratorHandler<TCommand>(decorator, handler);
                }

                return handler;
            });

            return services;
        }

        /// <summary>
        /// Registers a handler for the command/response pair, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="CommandHandlerOptions{TCommand, TResponse}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TCommand">The command type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="TResponse">The response type produced for the command.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddCommandHandler<TCommand, TResponse, THandler>(
            Action<CommandHandlerOptions<TCommand, TResponse>>? configure = null)
            where TCommand : ICommand<TResponse>
            where THandler : class, ICommandHandler<TCommand, TResponse>
        {
            services.AddTransient<THandler>();

            var options = new CommandHandlerOptions<TCommand, TResponse>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<ICommandHandler<TCommand, TResponse>>(pv =>
            {
                ICommandHandler<TCommand, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (ICommandDecorator.WithResponse)pv.GetRequiredService(decoratorType);
                    handler = new CommandDecoratorHandler<TCommand, TResponse>(decorator, handler);
                }

                return handler;
            });

            return services;
        }

        /// <summary>
        /// Registers a handler for the query, optionally wrapping it with a
        /// decorator pipeline configured via <see cref="QueryHandlerOptions{TQuery, TResponse}.Decorator"/>.
        /// </summary>
        /// <typeparam name="TQuery">The query type handled by <typeparamref name="THandler"/>.</typeparam>
        /// <typeparam name="TResponse">The response type produced for the query.</typeparam>
        /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
        /// <param name="configure">
        /// An optional callback to configure the handler, such as attaching decorators via
        /// <c>x.Decorator.With&lt;TDecorator&gt;()</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddQueryHandler<TQuery, TResponse, THandler>(
            Action<QueryHandlerOptions<TQuery, TResponse>>? configure = null)
            where TQuery : IQuery<TResponse>
            where THandler : class, IQueryHandler<TQuery, TResponse>
        {
            services.AddTransient<THandler>();

            var options = new QueryHandlerOptions<TQuery, TResponse>();
            configure?.Invoke(options);

            foreach (var decoratorType in options.Decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IQueryHandler<TQuery, TResponse>>(pv =>
            {
                IQueryHandler<TQuery, TResponse> handler = pv.GetRequiredService<THandler>();

                foreach (var decoratorType in options.Decorators)
                {
                    var decorator = (IQueryDecorator)pv.GetRequiredService(decoratorType);
                    handler = new QueryDecoratorHandler<TQuery, TResponse>(decorator, handler);
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

/// <summary>
/// Configures how a stream request handler is registered, most notably the decorator
/// pipeline applied around it.
/// </summary>
/// <typeparam name="TRequest">The stream request type being handled.</typeparam>
/// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
public sealed class StreamRequestHandlerOptions<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingStreamDecorator&gt;()</c>.
    /// </summary>
    public StreamDecoratorBuilder<TRequest, TResponse> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="StreamRequestHandlerOptions{TRequest, TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The stream request type being handled.</typeparam>
/// <typeparam name="TResponse">The type of each item produced for the request.</typeparam>
public sealed class StreamDecoratorBuilder<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private readonly StreamRequestHandlerOptions<TRequest, TResponse> _options;

    internal StreamDecoratorBuilder(StreamRequestHandlerOptions<TRequest, TResponse> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="IStreamRequestDecorator"/>.
    /// </typeparam>
    /// <returns>The same <see cref="StreamDecoratorBuilder{TRequest, TResponse}"/> so that calls can be chained.</returns>
    public StreamDecoratorBuilder<TRequest, TResponse> With<TDecorator>()
        where TDecorator : IStreamRequestDecorator
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}

/// <summary>
/// Configures how a command handler is registered, most notably the decorator pipeline
/// applied around it.
/// </summary>
/// <typeparam name="TCommand">The command type being handled.</typeparam>
public sealed class CommandHandlerOptions<TCommand>
    where TCommand : ICommand
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public CommandDecoratorBuilder<TCommand> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="CommandHandlerOptions{TCommand}"/>.
/// </summary>
/// <typeparam name="TCommand">The command type being handled.</typeparam>
public sealed class CommandDecoratorBuilder<TCommand>
    where TCommand : ICommand
{
    private readonly CommandHandlerOptions<TCommand> _options;

    internal CommandDecoratorBuilder(CommandHandlerOptions<TCommand> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="ICommandDecorator"/>.
    /// </typeparam>
    /// <returns>The same <see cref="CommandDecoratorBuilder{TCommand}"/> so that calls can be chained.</returns>
    public CommandDecoratorBuilder<TCommand> With<TDecorator>()
        where TDecorator : ICommandDecorator
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}

/// <summary>
/// Configures how a command/response handler is registered, most notably the decorator
/// pipeline applied around it.
/// </summary>
/// <typeparam name="TCommand">The command type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the command.</typeparam>
public sealed class CommandHandlerOptions<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public CommandDecoratorBuilder<TCommand, TResponse> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="CommandHandlerOptions{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TCommand">The command type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the command.</typeparam>
public sealed class CommandDecoratorBuilder<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private readonly CommandHandlerOptions<TCommand, TResponse> _options;

    internal CommandDecoratorBuilder(CommandHandlerOptions<TCommand, TResponse> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="ICommandDecorator.WithResponse"/>.
    /// </typeparam>
    /// <returns>The same <see cref="CommandDecoratorBuilder{TCommand, TResponse}"/> so that calls can be chained.</returns>
    public CommandDecoratorBuilder<TCommand, TResponse> With<TDecorator>()
        where TDecorator : ICommandDecorator.WithResponse
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}

/// <summary>
/// Configures how a query handler is registered, most notably the decorator pipeline
/// applied around it.
/// </summary>
/// <typeparam name="TQuery">The query type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the query.</typeparam>
public sealed class QueryHandlerOptions<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>x.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public QueryDecoratorBuilder<TQuery, TResponse> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a <see cref="QueryHandlerOptions{TQuery, TResponse}"/>.
/// </summary>
/// <typeparam name="TQuery">The query type being handled.</typeparam>
/// <typeparam name="TResponse">The response type produced for the query.</typeparam>
public sealed class QueryDecoratorBuilder<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    private readonly QueryHandlerOptions<TQuery, TResponse> _options;

    internal QueryDecoratorBuilder(QueryHandlerOptions<TQuery, TResponse> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap the handler. Decorators execute in the order added (FIFO):
    /// the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="IQueryDecorator"/>.
    /// </typeparam>
    /// <returns>The same <see cref="QueryDecoratorBuilder{TQuery, TResponse}"/> so that calls can be chained.</returns>
    public QueryDecoratorBuilder<TQuery, TResponse> With<TDecorator>()
        where TDecorator : IQueryDecorator
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}
