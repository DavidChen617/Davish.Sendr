---
title: Registering handlers
weight: 10
---

Covers manual registration for requests, commands, and queries. For streams, see the
"Streams" section below. For the source-generated alternative, see
[Enabling source generation](enabling-source-generation.md). Background on how these
kinds relate is in [Dispatch model](../concepts/dispatch-model.md).

## Requests

`IRequest` for a command with no return value:

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

`IRequest<TResponse>` for request/response:

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

Register both, then dispatch through `ISender`:

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<CreateOrder, CreateOrderHandler>()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>();

var sender = serviceProvider.GetRequiredService<ISender>();
await sender.SendAsync(new CreateOrder(Guid.NewGuid()), cancellationToken);
var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()), cancellationToken);
```

## Commands and queries (CQRS)

`ICommand`/`ICommand<TResponse>` and `IQuery<TResponse>` are separate contracts from
`IRequest`, with their own handler interfaces (`ICommandHandler`, `IQueryHandler`) and
registration methods (`AddCommandHandler`, `AddQueryHandler`). Use them when the codebase
wants an explicit CQRS vocabulary instead of the general-purpose `IRequest`.

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

```csharp
builder.Services
    .AddSendr()
    .AddCommandHandler<CreateOrder, CreateOrderHandler>()
    .AddQueryHandler<GetOrder, OrderDto, GetOrderHandler>();
```

Call sites still use `ISender.SendAsync` — it selects the `IRequest`, `ICommand`, or `IQuery`
overload from the argument's static type, so callers don't need to track which kind a
message belongs to.

## Streams

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

```csharp
builder.Services
    .AddSendr()
    .AddStreamRequestHandler<ListOrders, OrderDto, ListOrdersHandler>();

var streamSender = serviceProvider.GetRequiredService<IStreamSender>();

await foreach (var order in streamSender.SendStream(new ListOrders(), cancellationToken))
{
    Console.WriteLine(order.Number);
}
```

`ISender` also implements `IStreamSender`, so an existing `ISender` instance can dispatch
streams directly without resolving `IStreamSender` separately.

## Errors

- Registering a second handler for the same request/response pair (via any of
  `AddRequestHandler`, `AddCommandHandler`, `AddQueryHandler`, or `AddStreamRequestHandler`)
  throws `InvalidOperationException`.
- Dispatching a request with no registered handler throws `InvalidOperationException`,
  naming the missing registration call in its message. For streams, this happens when
  enumeration starts, not when `SendStream` is called.

See [Exceptions and diagnostics](../reference/exceptions-and-diagnostics.md) for the exact
messages.
