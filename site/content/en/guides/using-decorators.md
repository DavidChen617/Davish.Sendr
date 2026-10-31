---
title: Using decorators
weight: 20
---

Background on the pipeline model and ordering rules is in
[Decorator pipeline](../concepts/decorator-pipeline.md). This guide shows the manual and
generated ways to attach one.

## Manual: `x.Decorator.With<T>()`

Pass a `configure` delegate to the registration call and chain `With<T>()` in outer-to-inner
order:

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>(x => x.Decorator
        .With<TransactionDecorator>()
        .With<LoggingDecorator>());
```

Here `TransactionDecorator` is outermost (runs first and last); `LoggingDecorator` is nested
inside it.

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

Implementing both `IRequestDecorator` and `IRequestDecorator.WithResponse` on one class lets
it wrap both `IRequest` and `IRequest<TResponse>` handlers. The same pattern applies to
`ICommandDecorator`/`ICommandDecorator.WithResponse`, `IQueryDecorator`,
`IStreamRequestDecorator`, and `INotificationDecorator` — see
[Publishing notifications](publishing-notifications.md) for the notification-specific setup.

Commands use the same shape with `ICommandDecorator`:

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

Streams use `IStreamRequestDecorator`:

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

```csharp
builder.Services
    .AddSendr()
    .AddStreamRequestHandler<ListOrders, OrderDto, ListOrdersHandler>(x =>
        x.Decorator.With<LoggingStreamDecorator>());
```

> A stream decorator that uses `yield` should wrap the enumeration in `try`/`finally` and
> forward the token via `[EnumeratorCancellation]` so cancellation and disposal propagate
> correctly.

## Generated: `[DecorateWith<...>]`

When using [source-generated registration](enabling-source-generation.md), declare
decorators on the handler class itself. Type arguments run outer to inner, in the same order
as the equivalent `With<T>()` chain:

```csharp
[DecorateWith<TransactionDecorator, LoggingDecorator>]
public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder request, CancellationToken cancellationToken) { /* ... */ }
}
```

`DecorateWith<...>` comes in arities 1 through 8; apply at most one per handler class. The
generator validates the attribute's type arguments against the decorator interface required
for that handler's message kind, rejecting a mismatch at compile time (`SENDR001`) — see
[Exceptions and diagnostics](../reference/exceptions-and-diagnostics.md).
