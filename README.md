<div align="center">

# Davish.Sendr

*A free, lightweight mediator for .NET — explicit, no assembly scanning.*

[![NuGet](https://img.shields.io/nuget/v/Davish.Sendr.svg)](https://www.nuget.org/packages/Davish.Sendr/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

Sendr keeps the ergonomics you expect from a mediator — send a request, let a handler resolve it, wrap it in cross-cutting behaviour — while staying small, allocation-conscious, and fully explicit about what is registered. It covers request/response dispatching, async streams, and a decorator pipeline.

## Features

- **Request/response dispatching** — `IRequest` for commands, `IRequest<TResponse>` for queries.
- **Async streams** — `IStreamRequest<TResponse>` dispatched lazily as `IAsyncEnumerable<T>`.
- **Non-generic decorators** — a single decorator type wraps *any* compatible request; no per-request boilerplate.
- **Explicit registration** — every handler is registered by hand. No reflection-based assembly scanning, no surprises at startup.
- **Multi-target** — builds for `netstandard2.0` and `net10.0`.
- **Split packages** — depend only on `Davish.Sendr.Abstractions` from your domain layer.

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

## Benchmarks

The repository includes a [BenchmarkDotNet](https://benchmarkdotnet.org/) suite that compares dispatching through `ISender` against a direct handler call, and measures the overhead added by decorators and stream enumeration.

```bash
dotnet run -c Release --project tests/Benchmark
```

## Building from source

```bash
dotnet build
dotnet test
```
