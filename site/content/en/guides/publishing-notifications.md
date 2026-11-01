---
title: Publishing notifications
weight: 30
---

Background on Sequence/Parallel semantics and exception aggregation is in
[Notification model](../concepts/notification-model.md).

## Define the notification and its handlers

```csharp
public sealed record OrderPlaced(Guid OrderId) : INotification;

public sealed class ReserveInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class SendConfirmationEmailHandler : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
```

## Register: manual

Call `AddSendrNotification()` once, then register every handler for a notification type in a
single `AddNotificationHandler` call, assigning each one to the Sequence and/or Parallel
group:

```csharp
builder.Services
    .AddSendrNotification()
    .AddNotificationHandler<OrderPlaced>(x =>
    {
        x.Handler.Sequence.With<ReserveInventoryHandler>();
        x.Handler.Parallel.With<SendConfirmationEmailHandler>();
    });
```

## Publish

```csharp
var publisher = serviceProvider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(new OrderPlaced(order.Id), cancellationToken);
```

`PublishAsync` accepts the non-generic `INotification`, so a polymorphic batch — for example,
events read back from an outbox — can be published without the caller knowing each concrete
notification type. A notification with no registered handlers is a no-op.

## Decorating a specific handler entry

Each handler entry can carry its own decorator pipeline:

```csharp
builder.Services
    .AddSendrNotification()
    .AddNotificationHandler<OrderPlaced>(x => x.Handler.Sequence
        .With<ReserveInventoryHandler>(h => h.Decorator.With<LoggingNotificationDecorator>()));
```

```csharp
public sealed class LoggingNotificationDecorator(ILogger<LoggingNotificationDecorator> logger)
    : INotificationDecorator
{
    public async Task HandleAsync<TNotification>(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        logger.LogInformation("Handling {Notification}", typeof(TNotification).Name);
        await next();
        logger.LogInformation("Handled {Notification}", typeof(TNotification).Name);
    }
}
```

## Register: generated

`Davish.Sendr.Generators` also discovers `INotificationHandler<T>` implementations. Multiple
classes may handle the same notification without triggering the ambiguous-handler diagnostic
(`SENDR002`) — there's no per-handler execution-mode attribute; instead, `RunAs` sets how
*every* handler of the same notification runs, throughout the generated publisher. The
default is `Sequence`:

```csharp
builder.Services.AddSendrNotification(o => o.UseGenerators());

// Or, to run same-notification handlers concurrently instead:
builder.Services.AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel)));
```

Neither mode guarantees relative order between handlers of the same notification. Decorators
are declared the same way as elsewhere in generated registration:

```csharp
[DecorateWith<LoggingNotificationDecorator>]
public sealed class ReserveInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken) { /* ... */ }
}
```

Cross-assembly discovery works the same way as for `AddSendr`, with its own independent
assembly set:

```csharp
o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<ApplicationAssemblyMarker>());
```

See [Compile-time discovery](../concepts/compile-time-discovery.md) and
[Enabling source generation](enabling-source-generation.md) for the full discovery model.

> Manual `AddNotificationHandler` and generated registration are mutually exclusive for a
> given `IPublisher`: once `UseGenerators()` installs the generated publisher, it only
> dispatches to handlers discovered at compile time.
