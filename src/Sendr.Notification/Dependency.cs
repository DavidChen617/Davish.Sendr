using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Sendr.Notification services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class NotificationDependency
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="IPublisher"/> service so notifications can be published.
        /// Call this once during application startup, then register handlers with
        /// <see cref="AddNotificationHandler{TNotification}"/>.
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddSendrNotification()
        {
            services.AddSingleton<NotificationHandlersRegistry>();
            services.AddScoped<Publisher>();
            services.AddScoped<IPublisher>(sp => sp.GetRequiredService<Publisher>());
            return services;
        }

        /// <summary>
        /// Registers every handler for a notification type in a single call, most notably the
        /// Sequence group configured via <see cref="NotificationHandlerOptions{TNotification}.Handler"/>.
        /// Calling this more than once for the same <typeparamref name="TNotification"/> throws,
        /// since the Sequence's order is only meaningful when declared in one place.
        /// </summary>
        /// <typeparam name="TNotification">The notification type being handled.</typeparam>
        /// <param name="configure">
        /// Configures the handlers for <typeparamref name="TNotification"/>, for example
        /// <c>x.Handler.Sequence.With&lt;SomeHandler&gt;(h => h.Decorator.With&lt;LoggingDecorator&gt;())</c>.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection"/> so that calls can be chained.</returns>
        public IServiceCollection AddNotificationHandler<TNotification>(
            Action<NotificationHandlerOptions<TNotification>> configure)
            where TNotification : INotification
        {
            if (services.Any(d => d.ServiceType == typeof(NotificationHandlers<TNotification>)))
                throw new InvalidOperationException(
                    $"Notification handlers for '{typeof(TNotification)}' are already registered. " +
                    $"Register every handler for a notification in a single AddNotificationHandler<{typeof(TNotification).Name}> call.");

            var options = new NotificationHandlerOptions<TNotification>();
            configure(options);

            foreach (var handlerType in options.HandlerTypes)
                services.TryAddTransient(handlerType);

            foreach (var decoratorType in options.DecoratorTypes)
                services.TryAddTransient(decoratorType);

            services.AddSingleton(new NotificationHandlers<TNotification>(options.SequenceSteps, options.ParallelSteps));

            return services;
        }
    }
}

/// <summary>
/// Configures every handler registered for a notification type via <c>AddNotificationHandler</c>.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class NotificationHandlerOptions<TNotification>
    where TNotification : INotification
{
    internal List<Type> HandlerTypes { get; } = new();
    internal List<Type> DecoratorTypes { get; } = new();
    internal List<Func<IServiceProvider, TNotification, CancellationToken, Task>> SequenceSteps { get; } = new();
    internal List<Func<IServiceProvider, TNotification, CancellationToken, Task>> ParallelSteps { get; } = new();

    /// <summary>
    /// Fluent entry point for declaring this notification's handler groups, for example
    /// <c>x.Handler.Sequence.With&lt;SomeHandler&gt;()</c>.
    /// </summary>
    public NotificationHandlerBuilder<TNotification> Handler => new(this);

    internal Func<IServiceProvider, TNotification, CancellationToken, Task> BuildStep<THandler>(
        Stack<Type> decorators)
        where THandler : class, INotificationHandler<TNotification>
    {
        HandlerTypes.Add(typeof(THandler));

        foreach (var decoratorType in decorators)
            DecoratorTypes.Add(decoratorType);

        return (sp, notification, cancellationToken) =>
        {
            INotificationHandler<TNotification> handler = sp.GetRequiredService<THandler>();

            foreach (var decoratorType in decorators)
            {
                var decorator = (INotificationDecorator)sp.GetRequiredService(decoratorType);
                handler = new NotificationDecoratorHandlerImpl<TNotification>(decorator, handler);
            }

            return handler.HandleAsync(notification, cancellationToken);
        };
    }
}

/// <summary>
/// Fluent builder for declaring a notification's handler groups.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class NotificationHandlerBuilder<TNotification>
    where TNotification : INotification
{
    private readonly NotificationHandlerOptions<TNotification> _options;

    internal NotificationHandlerBuilder(NotificationHandlerOptions<TNotification> options) => _options = options;

    /// <summary>
    /// Fluent entry point for declaring handlers that run one after another, in the order added,
    /// stopping if one of them throws.
    /// </summary>
    public SequenceHandlerBuilder<TNotification> Sequence => new(_options);

    /// <summary>
    /// Fluent entry point for declaring handlers that run concurrently with each other and with
    /// the Sequence group.
    /// </summary>
    /// <remarks>
    /// Placeholder: today this runs every entry via <c>Task.WhenAll</c> with no further policy
    /// (exception aggregation, per-handler scoping for concurrent access to scoped services such
    /// as a <c>DbContext</c>, cancellation-on-failure). Those are expected to be refined later.
    /// </remarks>
    public ParallelHandlerBuilder<TNotification> Parallel => new(_options);
}

/// <summary>
/// Fluent builder for declaring a notification's Sequence group: handlers that run one after
/// another, in the order added.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class SequenceHandlerBuilder<TNotification>
    where TNotification : INotification
{
    private readonly NotificationHandlerOptions<TNotification> _options;

    internal SequenceHandlerBuilder(NotificationHandlerOptions<TNotification> options) => _options = options;

    /// <summary>
    /// Adds a handler to the end of the Sequence, optionally wrapping it with a decorator
    /// pipeline configured via <see cref="NotificationHandlerEntryOptions{TNotification}.Decorator"/>.
    /// </summary>
    /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
    /// <param name="configure">
    /// An optional callback to configure this handler entry, such as attaching decorators via
    /// <c>h.Decorator.With&lt;TDecorator&gt;()</c>.
    /// </param>
    /// <returns>The same <see cref="SequenceHandlerBuilder{TNotification}"/> so that calls can be chained.</returns>
    public SequenceHandlerBuilder<TNotification> With<THandler>(
        Action<NotificationHandlerEntryOptions<TNotification>>? configure = null)
        where THandler : class, INotificationHandler<TNotification>
    {
        var entryOptions = new NotificationHandlerEntryOptions<TNotification>();
        configure?.Invoke(entryOptions);

        _options.SequenceSteps.Add(_options.BuildStep<THandler>(entryOptions.Decorators));

        return this;
    }
}

/// <summary>
/// Fluent builder for declaring a notification's Parallel group: handlers that run concurrently
/// with each other and with the Sequence group.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class ParallelHandlerBuilder<TNotification>
    where TNotification : INotification
{
    private readonly NotificationHandlerOptions<TNotification> _options;

    internal ParallelHandlerBuilder(NotificationHandlerOptions<TNotification> options) => _options = options;

    /// <summary>
    /// Adds a handler to the Parallel group, optionally wrapping it with a decorator pipeline
    /// configured via <see cref="NotificationHandlerEntryOptions{TNotification}.Decorator"/>.
    /// </summary>
    /// <typeparam name="THandler">The concrete handler implementation to register.</typeparam>
    /// <param name="configure">
    /// An optional callback to configure this handler entry, such as attaching decorators via
    /// <c>h.Decorator.With&lt;TDecorator&gt;()</c>.
    /// </param>
    /// <returns>The same <see cref="ParallelHandlerBuilder{TNotification}"/> so that calls can be chained.</returns>
    public ParallelHandlerBuilder<TNotification> With<THandler>(
        Action<NotificationHandlerEntryOptions<TNotification>>? configure = null)
        where THandler : class, INotificationHandler<TNotification>
    {
        var entryOptions = new NotificationHandlerEntryOptions<TNotification>();
        configure?.Invoke(entryOptions);

        _options.ParallelSteps.Add(_options.BuildStep<THandler>(entryOptions.Decorators));

        return this;
    }
}

/// <summary>
/// Configures a single handler entry within a notification's Sequence, most notably the
/// decorator pipeline applied around it.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class NotificationHandlerEntryOptions<TNotification>
    where TNotification : INotification
{
    internal Stack<Type> Decorators { get; } = new();

    /// <summary>
    /// Fluent entry point for attaching decorators, for example
    /// <c>h.Decorator.With&lt;LoggingDecorator&gt;()</c>.
    /// </summary>
    public NotificationDecoratorBuilder<TNotification> Decorator => new(this);
}

/// <summary>
/// Fluent builder for adding decorators to a single <see cref="NotificationHandlerEntryOptions{TNotification}"/>.
/// </summary>
/// <typeparam name="TNotification">The notification type being handled.</typeparam>
public sealed class NotificationDecoratorBuilder<TNotification>
    where TNotification : INotification
{
    private readonly NotificationHandlerEntryOptions<TNotification> _options;

    internal NotificationDecoratorBuilder(NotificationHandlerEntryOptions<TNotification> options) => _options = options;

    /// <summary>
    /// Adds a decorator to wrap this handler entry. Decorators execute in the order added
    /// (FIFO): the first decorator added forms the outermost layer and executes first.
    /// </summary>
    /// <typeparam name="TDecorator">
    /// The decorator implementation, which must implement <see cref="INotificationDecorator"/>.
    /// </typeparam>
    /// <returns>The same <see cref="NotificationDecoratorBuilder{TNotification}"/> so that calls can be chained.</returns>
    public NotificationDecoratorBuilder<TNotification> With<TDecorator>()
        where TDecorator : INotificationDecorator
    {
        _options.Decorators.Push(typeof(TDecorator));
        return this;
    }
}
