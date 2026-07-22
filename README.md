<div align="center">

# Davish.Sendr

*A free, lightweight mediator for .NET — explicit, no assembly scanning.*

[![NuGet](https://img.shields.io/nuget/v/Davish.Sendr.svg)](https://www.nuget.org/packages/Davish.Sendr/)
[![NuGet](https://img.shields.io/nuget/v/Davish.Sendr.Notification.svg?label=nuget%20%28notification%29)](https://www.nuget.org/packages/Davish.Sendr.Notification/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

Sendr keeps the ergonomics you expect from a mediator — send a request, let a handler resolve it, wrap it in cross-cutting behaviour — while staying small, allocation-conscious, and fully explicit about what is registered. It covers request/response dispatching, async streams, notification fan-out, and a decorator pipeline that works across all three.

## Features

- **Request/response dispatching** — `IRequest` for commands, `IRequest<TResponse>` for queries, each resolved to exactly one handler.
- **Async streams** — `IStreamRequest<TResponse>` dispatched lazily as `IAsyncEnumerable<T>`.
- **Notification fan-out** — `INotification` published to any number of handlers, arranged into an ordered **Sequence** group and a concurrent **Parallel** group.
- **Non-generic decorators** — a single decorator type wraps *any* compatible request, stream, or notification handler; no per-type boilerplate.
- **Explicit registration** — every handler is registered by hand. No reflection-based assembly scanning, no surprises at startup.
- **Multi-target** — builds for `netstandard2.0` and `net10.0`.
- **Split packages** — depend only on the abstractions package from your domain layer; request/response and notification each ship as their own pair of packages.

> [!NOTE]
> Unlike scanning-based mediators, Sendr never discovers handlers implicitly. Registration is a compile-time-checked call, so a missing handler is obvious at the composition root.

## Install

```bash
dotnet add package Davish.Sendr
```

The contracts (`IRequest`, `IRequestHandler`, `IRequestDecorator`, `ISender`, …) also ship on their own so your domain assemblies can reference them without pulling in the DI implementation:

```bash
dotnet add package Davish.Sendr.Abstractions
```

Notification publishing is a separate pair of packages — it doesn't depend on `Davish.Sendr`, so you can add it on its own:

```bash
dotnet add package Davish.Sendr.Notification
dotnet add package Davish.Sendr.Notification.Abstractions
```

## Getting started

Call `AddSendr` once, then register each handler explicitly.

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<CreateOrder, CreateOrderHandler>()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>()
    .AddStreamRequestHandler<ListOrders, OrderDto, ListOrdersHandler>();
```

## Requests

Use `IRequest` for commands that do not return a value.

```csharp
public sealed record CreateOrder(Guid Id) : IRequest;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder>
{
    public Task HandleAsync(CreateOrder request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
```

Use `IRequest<TResponse>` for request/response dispatching.

```csharp
public sealed record GetOrder(Guid Id) : IRequest<OrderDto>;

public sealed record OrderDto(Guid Id, string Number);

public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrderDto(request.Id, "SO-001"));
    }
}
```

Resolve `ISender` and call `SendAsync`.

```csharp
var sender = serviceProvider.GetRequiredService<ISender>();

await sender.SendAsync(new CreateOrder(Guid.NewGuid()));

var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()));
```

## Decorators

Decorators are non-generic pipeline behaviours. A single decorator type can wrap any compatible request type — implement `IRequestDecorator` for commands and `IRequestDecorator.WithResponse` for queries.

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>(x => x.Decorator
        .With<TransactionDecorator>()
        .With<LoggingDecorator>());
```

Decorators execute in the order they are added: the first `With<>` is the outermost layer. In the example above, `TransactionDecorator` runs first and last, with `LoggingDecorator` nested inside it.

```csharp
public sealed class LoggingDecorator(ILogger<LoggingDecorator> logger)
    : IRequestDecorator, IRequestDecorator.WithResponse
{
    public async Task HandleAsync<TRequest>(
        TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        await next();
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
    }

    public async Task<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

## Streams

Use `IStreamRequest<TResponse>` and `IStreamRequestHandler<TRequest, TResponse>` for async streams. The sequence is lazy — handling begins when enumeration starts.

```csharp
public sealed record ListOrders : IStreamRequest<OrderDto>;

public sealed class ListOrdersHandler : IStreamRequestHandler<ListOrders, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> HandleAsync(
        ListOrders request,
        CancellationToken cancellationToken = default)
    {
        yield return new OrderDto(Guid.NewGuid(), "SO-001");
        await Task.Delay(10, cancellationToken);
        yield return new OrderDto(Guid.NewGuid(), "SO-002");
    }
}
```

Resolve `IStreamSender` and call `SendStream`.

```csharp
var streamSender = serviceProvider.GetRequiredService<IStreamSender>();

await foreach (var order in streamSender.SendStream(new ListOrders()))
{
    Console.WriteLine(order.Number);
}
```

Stream handlers support decorators too, via `IStreamRequestDecorator`.

```csharp
builder.Services
    .AddSendr()
    .AddStreamRequestHandler<ListOrders, OrderDto, ListOrdersHandler>(x =>
        x.Decorator.With<LoggingStreamDecorator>());
```

```csharp
public sealed class LoggingStreamDecorator(ILogger<LoggingStreamDecorator> logger)
    : IStreamRequestDecorator
{
    public IAsyncEnumerable<TResponse> HandleAsync<TRequest, TResponse>(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TResponse>
    {
        logger.LogInformation("Streaming {Request}", typeof(TRequest).Name);
        return next();
    }
}
```

> [!TIP]
> A stream decorator that uses `yield` should wrap the enumeration in `try/finally` and forward the token via `[EnumeratorCancellation]` so cancellation and disposal propagate correctly.

## Notifications

Unlike a request, a notification can have any number of handlers — including zero. Use `INotification` for events you want to fan out, `INotificationHandler<TNotification>` for each handler, and `IPublisher` to publish.

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

Call `AddSendrNotification` once, then register every handler for a notification type in a single `AddNotificationHandler` call, arranging them into a **Sequence** (run one after another, in order, stopping if one throws) and/or a **Parallel** group (run concurrently).

```csharp
builder.Services
    .AddSendrNotification()
    .AddNotificationHandler<OrderPlaced>(x =>
    {
        x.Handler.Sequence.With<ReserveInventoryHandler>();
        x.Handler.Parallel.With<SendConfirmationEmailHandler>();
    });
```

Resolve `IPublisher` and call `PublishAsync`. It takes the non-generic `INotification`, so a batch collected polymorphically — for example from an outbox — can be published without knowing each concrete type; publishing a notification with no registered handlers is a no-op.

```csharp
var publisher = serviceProvider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(new OrderPlaced(order.Id));
```

Each handler entry can have its own decorator pipeline via `INotificationDecorator`, configured the same way as request decorators.

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

> [!IMPORTANT]
> The Parallel group is still evolving. Handlers run concurrently via `Task.WhenAll`, so write them as proper `async` methods — a handler that throws synchronously instead of through an awaited `Task` can prevent handlers queued after it from running. If more than one handler fails, only the first exception surfaces from `PublishAsync`. Handlers don't get an isolated DI scope either, so avoid sharing a non-thread-safe scoped service (such as a `DbContext`) across Parallel entries.

> [!NOTE]
> `AddNotificationHandler<TNotification>` can only be called once per notification type — it throws on a second call, since the Sequence's order is only meaningful when every handler for that notification is declared together.
