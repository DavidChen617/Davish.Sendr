<div align="center">

# Davish.Sendr

*A free, lightweight mediator for .NET — explicit, no assembly scanning.*

[![NuGet](https://img.shields.io/nuget/v/Davish.Sendr.svg)](https://www.nuget.org/packages/Davish.Sendr/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

Sendr keeps the ergonomics you expect from a mediator — send a request, let a handler resolve it, wrap it in cross-cutting behaviour — while staying small, allocation-conscious, and fully explicit about what is registered. It covers request/response dispatching, async streams, notification fan-out, and a decorator pipeline that works across all three.

## Features

- **Request/response dispatching** — `IRequest` for commands, `IRequest<TResponse>` for queries, each resolved to exactly one handler.
- **CQRS contracts** — `ICommand` / `ICommand<TResponse>` and `IQuery<TResponse>` name the command/query split explicitly, while still dispatching through the same `ISender`.
- **Async streams** — `IStreamRequest<TResponse>` dispatched lazily as `IAsyncEnumerable<T>`.
- **Notification fan-out** — `INotification` published to any number of handlers, arranged into an ordered **Sequence** group and a concurrent **Parallel** group.
- **Non-generic decorators** — a single decorator type wraps *any* compatible request, stream, or notification handler; no per-type boilerplate.
- **Explicit registration** — every handler is registered by hand. No reflection-based assembly scanning, no surprises at startup.
- **Optional source-generated dispatch** — `Davish.Sendr.Generators` discovers handlers at compile time and installs a reflection-free `ISender`/`IStreamSender`; opt in with `AddSendr(o => o.UseGenerators())`.
- **Multi-target** — builds for `netstandard2.0` and `net10.0`.
- **Split packages** — depend only on `Davish.Sendr.Abstractions` from your domain layer; the implementation ships in `Davish.Sendr` alone, request/response and notification together.

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

Notification publishing (`INotification`, `IPublisher`, `AddSendrNotification`, …) ships in these same two packages — no extra package needed.

If you want the CQRS naming (`ICommand`, `IQuery`, …) instead of the plain `IRequest` contracts, it's already there too — `ICommand`/`ICommandHandler`/`IQuery`/`IQueryHandler` ship in `Davish.Sendr.Abstractions` itself.

> [!NOTE]
> `Davish.Sendr.Message` and `Davish.Sendr.Notification`/`Davish.Sendr.Notification.Abstractions` are deprecated — those types used to live there. The packages still resolve (they now just reference `Davish.Sendr.Abstractions`/`Davish.Sendr`), so existing references keep compiling, but new projects should reference `Davish.Sendr.Abstractions`/`Davish.Sendr` directly instead.

Compile-time handler discovery and dispatch (opt-in — see [Source-generated registration](#source-generated-registration-opt-in)):

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
> Only one handler may be registered per request/response pair — a second `AddRequestHandler`/`AddCommandHandler`/`AddQueryHandler`/`AddStreamRequestHandler` call for the same type and response throws `InvalidOperationException` instead of silently replacing the first. Calling `SendAsync`/`SendStream` for a type with no handler registered throws `InvalidOperationException` naming the registration call that's missing, rather than a bare DI "no service registered" message. Each registration is frozen at the point it's made — the `configure` callback's parameter isn't meant to be kept and mutated afterward; doing so has no effect on a provider already built from it.

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

`ICommand` / `ICommand<TResponse>` and `IQuery<TResponse>` are their own hierarchy, independent of `IRequest` / `IRequest<TResponse>` — not a wrapper over it. Each has its own handler interface, its own `ISender.SendAsync` overload, its own registration method, and its own decorator interface (`ICommandDecorator` / `IQueryDecorator`), so a command or query handler never implements `IRequestHandler`.

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

Register with `AddCommandHandler`/`AddQueryHandler` instead of `AddRequestHandler`. `ISender.SendAsync` still resolves the right overload from the argument's static type — call sites don't change.

```csharp
builder.Services
    .AddSendr()
    .AddCommandHandler<CreateOrder, CreateOrderHandler>()
    .AddQueryHandler<GetOrder, OrderDto, GetOrderHandler>();
```

Decorators for commands/queries implement `ICommandDecorator`/`IQueryDecorator` rather than `IRequestDecorator` — the same shape, just constrained to `ICommand`/`IQuery` instead of `IRequest`:

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

## Source-generated registration (opt-in)

`Davish.Sendr.Generators` discovers your `IRequestHandler`/`IStreamRequestHandler` implementations at compile time and generates `UseGenerators()`, a `SendrOptions` extension that plugs into `AddSendr` and replaces every manual `AddRequestHandler`/`AddStreamRequestHandler` call, backed by a reflection-free `ISender`/`IStreamSender` — dispatch is a compile-time-built `Dictionary<Type, Func<...>>` lookup, not `MakeGenericType` + compiled expression trees.

```xml
<PackageReference Include="Davish.Sendr" Version="3.3.0" />
<PackageReference Include="Davish.Sendr.Generators" Version="1.1.5" PrivateAssets="all" />
```

```csharp
builder.Services.AddSendr(o => o.UseGenerators());
```

Decorators are declared on the handler with `[DecorateWith<...>]` instead of a fluent `configure` callback. Type arguments run outer to inner — the first one runs first, matching `x.Decorator.With<T>()` ordering:

```csharp
[DecorateWith<TransactionDecorator, LoggingDecorator>]
public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder request, CancellationToken cancellationToken) { /* ... */ }
}
```

Notes:

- This is purely additive — `AddSendr()` without `UseGenerators()` keeps registering the default reflection-based sender unchanged.
- `o.UseGenerators()` and manual `AddRequestHandler`/`AddStreamRequestHandler` calls don't mix for the *same* request type: once `UseGenerators()` installs the generated sender, dispatch only knows about handlers discovered at compile time. Use `SendrOptions.UseSender<TSender>()` directly if you ever need to plug in your own sender implementation the same way.
- `[DecorateWith<...>]` comes in arities 1 through 8; apply at most one per handler class.
- Handler classes and records are both discovered — not just `class`.
- A handler declared as `struct`/`record struct` is a compile error (`SENDR004`), since handler resolution (generated or manual) requires a reference type; use `class`/`record class` instead.
- Generic (open) handler classes aren't discovered — register those manually with `AddRequestHandler`/`AddStreamRequestHandler` (without `UseGenerators()`).
- By default, a handler only counts if it's declared in the *same project* that calls `UseGenerators()`: an incremental generator only ever inspects its own compilation's syntax trees, never the already-compiled output of a referenced project, so a handler living in a library referenced via `ProjectReference` (or a NuGet package) is invisible on its own even though it compiles and links fine. Name that library's assembly explicitly with `IncludeAssemblyOf<TMarker>()` (see [Cross-assembly discovery](#cross-assembly-discovery) below) to have the generator walk its metadata too, or register that library's handlers manually with `AddRequestHandler`/etc. instead.
- A duplicate handler for the same request/response pair is a compile error (`SENDR002`), not a silent pick. A request type implementing `IRequest<TResponse>` (or `ICommand<TResponse>`/`IQuery<TResponse>`/`IStreamRequest<TResponse>`) for more than one `TResponse` is not a duplicate — each `TResponse` gets its own handler slot.
- `ICommandHandler`/`IQueryHandler` are discovered the same way — `[DecorateWith<...>]` on a command/query handler validates against `ICommandDecorator`/`IQueryDecorator` instead.
- `INotificationHandler` is covered too — see [Notification: source-generated registration](#notification-source-generated-registration-opt-in) below, since it plugs into `AddSendrNotification` rather than `AddSendr`.

### Cross-assembly discovery

Name another project's assembly explicitly to have the generator discover its handlers too — for example a domain/application layer referenced via `ProjectReference`, whose own build never runs `Davish.Sendr.Generators`:

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

- This is a compile-time-only declaration read directly from source by the generator — it does not perform any runtime assembly scanning, and it only recognizes a call written directly inline in the `UseGenerators(g => ...)` lambda (a fluent chain, or one call per statement in a block lambda). A delegate variable, method group, loop, conditional, or helper/extension method wrapping the configuration isn't statically resolvable and is a compile error (`SENDR005`).
- The current project is always included automatically; naming its own assembly again via a local marker is harmless, not an error.
- Only the assemblies named directly are walked — an included assembly's own further dependencies are not pulled in transitively.
- A handler discovered this way must be accessible from the calling project: public (including every containing type, for a nested handler), or internal with a matching `[assembly: InternalsVisibleTo("...")]` declared by the library. Otherwise it's a compile error (`SENDR007`) rather than an invisible handler.
- `ISender`/`IStreamSender` and `IPublisher` each build their own single dispatch table per compilation, from their own `UseGenerators()` call's inclusions — including the same library on both sides (one call for `AddSendr`, one for `AddSendrNotification`) is normal and expected. Every `UseGenerators()` call for the *same* one of those two, however, must declare the same assembly set; two calls that disagree are a compile error (`SENDR006`), since there both share that project's single generated dispatch table.
- A duplicate handler for the same request/response pair across the current project and an included assembly — or across two included assemblies — is `SENDR002`, the same diagnostic as two handlers declared locally.

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
> Both groups run every handler regardless of earlier failures — Sequence doesn't stop at the first throw, it just runs one handler at a time instead of concurrently. Every exception is collected: zero stay silent, exactly one is rethrown as itself (preserving its original stack trace), and two or more are combined into one `AggregateException` from `PublishAsync`. Handlers don't get an isolated DI scope either, so avoid sharing a non-thread-safe scoped service (such as a `DbContext`) across Parallel entries.

> [!NOTE]
> `AddNotificationHandler<TNotification>` can only be called once per notification type — it throws on a second call, since the Sequence's order is only meaningful when every handler for that notification is declared together. The registration is frozen at the point it's made — the `configure` callback's parameter isn't meant to be kept and mutated afterward; doing so has no effect on a provider already built from it.

## Notification: source-generated registration (opt-in)

`Davish.Sendr.Generators` also discovers your `INotificationHandler<T>` implementations at compile time and generates `UseGenerators()`, a `NotificationOptions` extension that plugs into `AddSendrNotification`, backed by a reflection-free `IPublisher`.

Unlike request/command/query handlers, any number of classes may handle the same notification type — that's the normal case, not a conflict — so there's no per-handler attribute and no `SENDR002` dedup for notifications. Instead, `RunAs` is a single, global choice for how handlers of the *same* notification run relative to each other, defaulting to `Sequence`:

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

- No ordering is guaranteed among handlers of the same notification, whether `Sequence` or `Parallel` — `RunAs` only chooses one-at-a-time vs. concurrent execution, not a priority between handlers.
- `o.UseGenerators()` and manual `AddNotificationHandler` calls don't mix for the *same* notification type, same as the request-side generator.
- Exception aggregation (the 0/1/2+ rule described above) is identical to the manual registration path — both call the same `NotificationGroupRunner`.
- `GeneratedNotificationRunOptions` has its own `IncludeAssemblyOf<TMarker>()`, chainable with `RunAs`, for the same [cross-assembly discovery](#cross-assembly-discovery) described above — `o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<ApplicationAssemblyMarker>())`. It has its own independent assembly set, so it doesn't need to match whatever `AddSendr`'s `UseGenerators()` declares.
