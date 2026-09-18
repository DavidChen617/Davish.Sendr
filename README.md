<div align="center">

# Davish.Sendr

*A free, lightweight mediator for .NET with explicit registration and no runtime assembly scanning.*

[![NuGet](https://img.shields.io/nuget/v/Davish.Sendr.svg)](https://www.nuget.org/packages/Davish.Sendr/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

Sendr is a small mediator library designed to limit allocations. It supports request/response dispatch, async streams, and notification publishing, with decorators for each. Register handlers explicitly or opt into compile-time discovery.

## Features

- Dispatch `IRequest` commands and `IRequest<TResponse>` queries to one handler per request/response pair.
- Use `ICommand`, `ICommand<TResponse>`, and `IQuery<TResponse>` for explicit CQRS contracts, dispatched through the same `ISender`.
- Return lazy async streams with `IStreamRequest<TResponse>` and `IAsyncEnumerable<T>`.
- Publish `INotification` to any number of handlers in ordered Sequence and concurrent Parallel groups.
- Wrap compatible request, stream, or notification handlers with a reusable, non-generic decorator.
- Register handlers manually or use compile-time discovery. Neither path scans assemblies at runtime.
- Enable reflection-free `ISender`/`IStreamSender` dispatch with `Davish.Sendr.Generators` and `AddSendr(o => o.UseGenerators())`.
- Target `netstandard2.0` and `net10.0`.
- Reference `Davish.Sendr.Abstractions` for domain contracts and `Davish.Sendr` for the request/response and notification implementations.

> [!NOTE]
> Manual registration uses generic constraints to check handler compatibility at compile time. The optional source generator discovers handlers in the current project and explicitly included assemblies. Missing request handlers are reported when dispatched; stream requests report them when enumeration starts.

## Install

```bash
dotnet add package Davish.Sendr
```

The contracts (`IRequest`, `IRequestHandler`, `IRequestDecorator`, `ISender`, …) also ship on their own so your domain assemblies can reference them without pulling in the DI implementation:

```bash
dotnet add package Davish.Sendr.Abstractions
```

Notification publishing (`INotification`, `IPublisher`, `AddSendrNotification`, …) is included in these two packages.

The CQRS contracts (`ICommand`, `ICommandHandler`, `IQuery`, and `IQueryHandler`) are also included in `Davish.Sendr.Abstractions`.

> [!NOTE]
> `Davish.Sendr.Message`, `Davish.Sendr.Notification`, and `Davish.Sendr.Notification.Abstractions` are deprecated compatibility packages. They reference `Davish.Sendr.Abstractions` or `Davish.Sendr`, so existing package references still compile. New projects should reference `Davish.Sendr.Abstractions` and `Davish.Sendr` directly.

For optional [compile-time handler discovery and dispatch](#source-generated-registration-opt-in), install:

```bash
dotnet add package Davish.Sendr.Generators
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

> [!NOTE]
> Register one handler per request/response pair. A second `AddRequestHandler`, `AddCommandHandler`, `AddQueryHandler`, or `AddStreamRequestHandler` call for the same type and response throws `InvalidOperationException`. Dispatching a request without a registered handler also throws `InvalidOperationException`, with a message naming the missing registration call. For streams, this happens when enumeration starts. Registration captures the configuration immediately; later changes to the object passed to `configure` have no effect.

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

await sender.SendAsync(new CreateOrder(Guid.NewGuid()), cancellationToken);

var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()), cancellationToken);
```

## Commands and queries (CQRS)

`ICommand` / `ICommand<TResponse>` and `IQuery<TResponse>` are independent of `IRequest` / `IRequest<TResponse>`. Each has its own handler interface, `ISender.SendAsync` overload, registration method, and decorator interface. Command and query handlers use `ICommandHandler` and `IQueryHandler` respectively, without requiring `IRequestHandler`.

```csharp
public sealed record CreateOrder(Guid Id) : ICommand;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder>
{
    public Task HandleAsync(CreateOrder command, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed record GetOrder(Guid Id) : IQuery<OrderDto>;

public sealed record OrderDto(Guid Id, string Number);

public sealed class GetOrderHandler : IQueryHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrderDto(query.Id, "SO-001"));
    }
}
```

Register with `AddCommandHandler` or `AddQueryHandler`. Call sites still use `ISender.SendAsync`, which selects the overload from the argument's static type.

```csharp
builder.Services
    .AddSendr()
    .AddCommandHandler<CreateOrder, CreateOrderHandler>()
    .AddQueryHandler<GetOrder, OrderDto, GetOrderHandler>();
```

Command and query decorators implement `ICommandDecorator` and `IQueryDecorator`. They follow the same pattern as `IRequestDecorator`, with constraints on `ICommand` and `IQuery`:

```csharp
public sealed class LoggingCommandDecorator(ILogger<LoggingCommandDecorator> logger) : ICommandDecorator
{
    public async Task HandleAsync<TCommand>(
        TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        logger.LogInformation("Handling {Command}", typeof(TCommand).Name);
        await next();
    }
}
```

## Decorators

Decorators are non-generic pipeline behaviours that can wrap any compatible request type. Use `IRequestDecorator` for `IRequest` and `IRequestDecorator.WithResponse` for `IRequest<TResponse>`. The separate CQRS contracts use `ICommandDecorator`, `ICommandDecorator.WithResponse`, and `IQueryDecorator`.

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

## Source-generated registration (opt-in)

`Davish.Sendr.Generators` discovers your request, command, query, and stream handler implementations at compile time and generates `UseGenerators()`, a `SendrOptions` extension that plugs into `AddSendr`. It registers the discovered handlers and installs a reflection-free `ISender`/`IStreamSender`. Dispatch uses generated dictionaries keyed by message type and, where applicable, response type.

```xml
<PackageReference Include="Davish.Sendr" Version="3.5.0" />
<PackageReference Include="Davish.Sendr.Generators" Version="2.1.0" PrivateAssets="all" />
```

```csharp
builder.Services.AddSendr(o => o.UseGenerators());
```

Declare decorators on the handler with `[DecorateWith<...>]`. Type arguments run from outermost to innermost, matching the order of `x.Decorator.With<T>()` calls:

```csharp
[DecorateWith<TransactionDecorator, LoggingDecorator>]
public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder request, CancellationToken cancellationToken) { /* ... */ }
}
```

Notes:

- `AddSendr()` without `UseGenerators()` registers the default reflection-based sender.
- Once `UseGenerators()` installs the generated sender, dispatch only knows about handlers discovered at compile time. Manual `AddRequestHandler`/`AddCommandHandler`/`AddQueryHandler`/`AddStreamRequestHandler` calls do not extend its dispatch tables. Choose manual registration or generated registration for the sender. Use `SendrOptions.UseSender<TSender>()` to install a custom sender implementation.
- `[DecorateWith<...>]` comes in arities 1 through 8; apply at most one per handler class.
- The generator discovers both classes and record classes.
- A handler declared as `struct`/`record struct` is a compile error (`SENDR004`), since handler resolution (generated or manual) requires a reference type; use `class`/`record class` instead.
- Open generic handler classes are not discovered. Register closed constructed handler types with the appropriate manual registration method, without `UseGenerators()`.
- By default, the generator discovers handlers only in the project that calls `UseGenerators()`. Handlers in a referenced project or NuGet package require an explicit `IncludeAssemblyOf<TMarker>()` declaration (see [Cross-assembly discovery](#cross-assembly-discovery)). Alternatively, use manual registration without `UseGenerators()`.
- Multiple handlers for the same request/response pair produce compile error `SENDR002`. A message may implement `IRequest<TResponse>`, `ICommand<TResponse>`, `IQuery<TResponse>`, or `IStreamRequest<TResponse>` with multiple response types; each response type has its own handler slot.
- The generator also discovers `ICommandHandler` and `IQueryHandler`. It validates their `[DecorateWith<...>]` attributes against the corresponding command or query decorator interface.
- For `INotificationHandler`, use `AddSendrNotification`. See [Notification: source-generated registration](#notification-source-generated-registration-opt-in).

### Cross-assembly discovery

Include another assembly explicitly to discover its handlers. For example, a domain or application library referenced through `ProjectReference` does not need to run the generator itself:

```csharp
// Any type declared in the library assembly works as the marker — it doesn't need to
// implement any particular interface.
public sealed class ApplicationAssemblyMarker;

builder.Services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<ApplicationAssemblyMarker>()));
```

`IncludeAssemblyOf<TMarker>()` chains, and combines with more than one assembly:

```csharp
o.UseGenerators(g => g
    .IncludeAssemblyOf<ApplicationAssemblyMarker>()
    .IncludeAssemblyOf<InfrastructureAssemblyMarker>());
```

Notes:

- The generator reads this declaration directly from source; it does not scan assemblies at runtime. Write calls inline in the `UseGenerators(g => ...)` lambda, either as a fluent chain or as individual statements in a block. Delegate variables, method groups, loops, conditionals, and helper or extension methods wrapping the configuration produce compile error `SENDR005`.
- The current project is always included automatically; naming its own assembly again via a local marker is harmless, not an error.
- Discovery includes only the named assemblies, not their transitive dependencies.
- A handler discovered this way must be accessible from the calling project: public (including every containing type, for a nested handler), or internal with a matching `[assembly: InternalsVisibleTo("...")]` declared by the library. Otherwise it's a compile error (`SENDR007`) rather than an invisible handler.
- Each compilation has one set of generated dispatch tables for `ISender`/`IStreamSender` and a separate set for `IPublisher`. Each uses the assemblies declared in its own `UseGenerators()` configuration. You can include the same library on both sides. Multiple calls configuring the same sender or publisher must declare identical assembly sets; conflicting sets produce compile error `SENDR006`.
- Duplicate handlers for a request/response pair produce `SENDR002` whether they are local, in an included assembly, or spread across assemblies.

## Streams

Use `IStreamRequest<TResponse>` and `IStreamRequestHandler<TRequest, TResponse>` for async streams. Handling begins when enumeration starts.

```csharp
public sealed record ListOrders : IStreamRequest<OrderDto>;

public sealed class ListOrdersHandler : IStreamRequestHandler<ListOrders, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> HandleAsync(
        ListOrders request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OrderDto(Guid.NewGuid(), "SO-001");
        await Task.Delay(10, cancellationToken);
        yield return new OrderDto(Guid.NewGuid(), "SO-002");
    }
}
```

Resolve `IStreamSender` and call `SendStream`. `ISender` also inherits this method, so an existing sender can dispatch streams directly.

```csharp
var streamSender = serviceProvider.GetRequiredService<IStreamSender>();

await foreach (var order in streamSender.SendStream(new ListOrders(), cancellationToken))
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

A notification can have any number of handlers, including zero. Define the event with `INotification`, implement each handler with `INotificationHandler<TNotification>`, and publish it through `IPublisher`.

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

Call `AddSendrNotification` once, then register every handler for a notification type in a single `AddNotificationHandler` call, arranging them into a **Sequence** (run one after another in registration order, continuing after failures) and/or a **Parallel** group (run concurrently). The two groups run concurrently with each other, so a Parallel handler does not wait for the Sequence group to finish.

```csharp
builder.Services
    .AddSendrNotification()
    .AddNotificationHandler<OrderPlaced>(x =>
    {
        x.Handler.Sequence.With<ReserveInventoryHandler>();
        x.Handler.Parallel.With<SendConfirmationEmailHandler>();
    });
```

Resolve `IPublisher` and call `PublishAsync`. It accepts the non-generic `INotification`, so you can publish a polymorphic batch, such as events from an outbox, without knowing each concrete type. Publishing a notification with no registered handlers is a no-op.

```csharp
var publisher = serviceProvider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(new OrderPlaced(order.Id), cancellationToken);
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
> Both groups run every handler even if an earlier handler fails. Sequence awaits handlers one at a time; Parallel starts them concurrently. `PublishAsync` collects all exceptions: a single exception is rethrown with its original stack trace, and multiple exceptions are combined into an `AggregateException`. Handlers share the calling DI scope, so Parallel handlers must not share a non-thread-safe scoped service such as a `DbContext`.

> [!NOTE]
> Call `AddNotificationHandler<TNotification>` once per notification type, with all handlers declared together so their Sequence order is explicit. A second call throws. Registration captures the configuration immediately; later changes to the object passed to `configure` have no effect.

## Notification: source-generated registration (opt-in)

`Davish.Sendr.Generators` also discovers your `INotificationHandler<T>` implementations at compile time and generates `UseGenerators()`, a `NotificationOptions` extension that plugs into `AddSendrNotification`, backed by a reflection-free `IPublisher`.

Multiple classes can handle the same notification, so they do not trigger the duplicate-handler diagnostic (`SENDR002`). There is no per-handler execution-mode attribute. Instead, `RunAs` sets how handlers of the same notification execute throughout the generated publisher. The default is `Sequence`:

```csharp
builder.Services.AddSendrNotification(o => o.UseGenerators());

// Or, to run same-notification handlers concurrently instead:
builder.Services.AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel)));
```

Decorators are declared with `[DecorateWith<...>]` the same way, validated against `INotificationDecorator`:

```csharp
[DecorateWith<LoggingNotificationDecorator>]
public sealed class ReserveInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken) { /* ... */ }
}
```

Notes:

- `RunAs` selects sequential or concurrent execution. Neither mode guarantees the relative order of handlers for the same notification.
- The generated publisher dispatches only to handlers discovered at compile time. Manual `AddNotificationHandler` calls do not extend its dispatch table; choose manual registration or generated registration for the publisher.
- Generated and manual dispatch use `NotificationGroupRunner`, with the same exception aggregation rules described above.
- `GeneratedNotificationRunOptions.IncludeAssemblyOf<TMarker>()` supports [cross-assembly discovery](#cross-assembly-discovery) and chains with `RunAs`: `o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<ApplicationAssemblyMarker>())`. Its assembly set is independent of the one configured for `AddSendr`.
