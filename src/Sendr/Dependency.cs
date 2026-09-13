using System.Runtime.ExceptionServices;
using Davish.Sendr.Implements;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Sendr services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class Dependency
{
    /// <summary>
    /// Disposes an already-constructed handler if a later decorator in its pipeline fails to
    /// resolve — otherwise the handler (already built via <c>ActivatorUtilities.CreateInstance</c>,
    /// so the container never captured it on its own) would never get disposed at all, since the
    /// registration's factory never returns anything for the container to track when it throws.
    /// Always throws: <paramref name="originalException"/> rethrown as itself if cleanup succeeds,
    /// or both combined into an <see cref="AggregateException"/> if cleanup also fails — a bare
    /// <c>throw;</c> after calling this would otherwise never run if disposal itself threw first,
    /// silently discarding the original decorator-construction failure and leaving only the
    /// secondary cleanup failure for the caller to see.
    /// </summary>
    private static void DisposeOnFailure(object? handler, Exception originalException)
    {
        try
        {
            switch (handler)
            {
                case IAsyncDisposable asyncDisposable:
                    // No async-returning factory overload exists for this DI registration shape,
                    // so this rare failure path (a decorator's own constructor throwing) blocks
                    // rather than leaking the handler.
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception cleanupException)
        {
            throw new AggregateException(
                "The decorator pipeline failed to construct, and cleaning up the " +
                "already-constructed handler also failed. See the inner exceptions for both.",
                originalException, cleanupException);
        }

        ExceptionDispatchInfo.Capture(originalException).Throw();
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="ISender"/> service so requests can be dispatched.
        /// Call this once during application startup, then register handlers with
        /// <see cref="AddRequestHandler{TRequest, TResponse, THandler}"/> — or pass
        /// <c>o => o.UseGenerators()</c> (from the <c>Davish.Sendr.Generators</c> package) to
        /// discover and register handlers at compile time instead.
        /// </summary>
        /// <param name="configure">
        /// An optional callback to configure Sendr, such as installing a generated or custom
        /// sender via <see cref="SendrOptions.UseSender{TSender}"/>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddSendr(Action<SendrOptions>? configure = null)
        {
            var options = new SendrOptions(services);
            configure?.Invoke(options);

            if (!options.HasCustomSender)
            {
                services.AddSingleton<HandlerRegistry>();

                // ISender is the only registration whose factory actually constructs Sender —
                // IStreamSender resolves through it instead of also constructing/resolving Sender
                // on its own. A third, separate Sender registration alongside ISender/IStreamSender
                // would mean the container captures the same disposable instance for disposal up
                // to three times over (once per registration whose factory returns it) if Sender
                // were ever made disposable; going through ISender's own resolution keeps that at
                // two — see SendrOptions.UseSender for the same reasoning applied to a custom
                // sender, where it matters today because a custom implementation commonly is
                // disposable.
                services.AddScoped<ISender>(sp => ActivatorUtilities.CreateInstance<Sender>(sp));
                services.AddScoped<IStreamSender>(sp => sp.GetRequiredService<ISender>());
            }

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
            var options = new RequestHandlerOptions<TRequest, TResponse>();
            configure?.Invoke(options);

            // Checked after configure runs, not before — see AddNotificationHandler for why: a
            // reentrant AddRequestHandler<TRequest, TResponse, ...> call for the same TRequest
            // from inside this configure callback would otherwise pass this same check too,
            // letting one registration silently shadow the other instead of throwing.
            if (services.Any(d => d.ServiceType == typeof(IRequestHandler<TRequest, TResponse>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TRequest)}' is already registered. Only one handler " +
                    $"may be registered per request type; remove the duplicate AddRequestHandler call.");

            // Snapshotted so the registered pipeline is frozen at this point: options.Decorators
            // is a plain mutable Stack<Type>, and the factory below is a closure over `options`
            // that runs on every future resolution, not just once now — if the caller kept a
            // reference to `options` (easy to do by capturing the configure callback's own
            // parameter) and pushed to it after BuildServiceProvider(), every AddRequestHandler
            // registration made from that same options instance would otherwise pick up the
            // change too, silently altering an already-built provider's dispatch behavior.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IRequestHandler<TRequest, TResponse>>(pv =>
            {
                // Constructed here (not resolved via a separate services.AddTransient<THandler>()
                // registration) so a disposable handler is captured for disposal exactly once by
                // this factory's own resolution, not twice — once here and once more from a
                // redundant standalone THandler registration.
                IRequestHandler<TRequest, TResponse> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (IRequestDecorator.WithResponse)pv.GetRequiredService(decoratorType);
                        handler = new DecoratorHandler<TRequest, TResponse>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
            var options = new RequestHandlerOptions<TRequest>();
            configure?.Invoke(options);

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this check runs after
            // configure instead of before.
            if (services.Any(d => d.ServiceType == typeof(IRequestHandler<TRequest>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TRequest)}' is already registered. Only one handler " +
                    $"may be registered per request type; remove the duplicate AddRequestHandler call.");

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this is snapshotted.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IRequestHandler<TRequest>>(pv =>
            {
                // See AddRequestHandler<TRequest, TResponse, THandler> for why this constructs
                // THandler directly instead of resolving it via its own registration.
                IRequestHandler<TRequest> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (IRequestDecorator)pv.GetRequiredService(decoratorType);
                        handler = new DecoratorHandler<TRequest>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
            var options = new StreamRequestHandlerOptions<TRequest, TResponse>();
            configure?.Invoke(options);

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this check runs after
            // configure instead of before.
            if (services.Any(d => d.ServiceType == typeof(IStreamRequestHandler<TRequest, TResponse>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TRequest)}' is already registered. Only one handler " +
                    $"may be registered per stream request type; remove the duplicate AddStreamRequestHandler call.");

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this is snapshotted.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IStreamRequestHandler<TRequest, TResponse>>(pv =>
            {
                // See AddRequestHandler<TRequest, TResponse, THandler> for why this constructs
                // THandler directly instead of resolving it via its own registration.
                IStreamRequestHandler<TRequest, TResponse> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (IStreamRequestDecorator)pv.GetRequiredService(decoratorType);
                        handler = new StreamDecoratorHandler<TRequest, TResponse>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
            var options = new CommandHandlerOptions<TCommand>();
            configure?.Invoke(options);

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this check runs after
            // configure instead of before.
            if (services.Any(d => d.ServiceType == typeof(ICommandHandler<TCommand>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TCommand)}' is already registered. Only one handler " +
                    $"may be registered per command type; remove the duplicate AddCommandHandler call.");

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this is snapshotted.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<ICommandHandler<TCommand>>(pv =>
            {
                // See AddRequestHandler<TRequest, TResponse, THandler> for why this constructs
                // THandler directly instead of resolving it via its own registration.
                ICommandHandler<TCommand> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (ICommandDecorator)pv.GetRequiredService(decoratorType);
                        handler = new CommandDecoratorHandler<TCommand>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
            var options = new CommandHandlerOptions<TCommand, TResponse>();
            configure?.Invoke(options);

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this check runs after
            // configure instead of before.
            if (services.Any(d => d.ServiceType == typeof(ICommandHandler<TCommand, TResponse>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TCommand)}' is already registered. Only one handler " +
                    $"may be registered per command type; remove the duplicate AddCommandHandler call.");

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this is snapshotted.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<ICommandHandler<TCommand, TResponse>>(pv =>
            {
                // See AddRequestHandler<TRequest, TResponse, THandler> for why this constructs
                // THandler directly instead of resolving it via its own registration.
                ICommandHandler<TCommand, TResponse> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (ICommandDecorator.WithResponse)pv.GetRequiredService(decoratorType);
                        handler = new CommandDecoratorHandler<TCommand, TResponse>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
            var options = new QueryHandlerOptions<TQuery, TResponse>();
            configure?.Invoke(options);

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this check runs after
            // configure instead of before.
            if (services.Any(d => d.ServiceType == typeof(IQueryHandler<TQuery, TResponse>)))
                throw new InvalidOperationException(
                    $"A handler for '{typeof(TQuery)}' is already registered. Only one handler " +
                    $"may be registered per query type; remove the duplicate AddQueryHandler call.");

            // See AddRequestHandler<TRequest, TResponse, THandler> for why this is snapshotted.
            var decorators = options.Decorators.ToArray();

            foreach (var decoratorType in decorators)
                services.TryAddTransient(decoratorType);

            services.AddTransient<IQueryHandler<TQuery, TResponse>>(pv =>
            {
                // See AddRequestHandler<TRequest, TResponse, THandler> for why this constructs
                // THandler directly instead of resolving it via its own registration.
                IQueryHandler<TQuery, TResponse> handler = ActivatorUtilities.CreateInstance<THandler>(pv);

                try
                {
                    foreach (var decoratorType in decorators)
                    {
                        var decorator = (IQueryDecorator)pv.GetRequiredService(decoratorType);
                        handler = new QueryDecoratorHandler<TQuery, TResponse>(decorator, handler);
                    }
                }
                catch (Exception ex)
                {
                    DisposeOnFailure(handler, ex);
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
