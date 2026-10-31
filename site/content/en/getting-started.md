---
title: Getting started
weight: 10
---

## Install

```bash
dotnet add package Davish.Sendr
```

If a domain/application assembly should reference the message contracts (`IRequest`,
`IRequestHandler`, `ISender`, …) without pulling in the DI implementation, reference the
contracts package instead:

```bash
dotnet add package Davish.Sendr.Abstractions
```

`Davish.Sendr` already depends on `Davish.Sendr.Abstractions`, so an application's entry
point project only needs the former.

## Register `AddSendr` and a handler

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<CreateOrder, CreateOrderHandler>()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>();
```

`AddRequestHandler<TRequest, THandler>` is for requests with no response
(`IRequest`); `AddRequestHandler<TRequest, TResponse, THandler>` is for requests that return
a value (`IRequest<TResponse>`). Register at most one handler per request/response pair —
see [Dispatch model](concepts/dispatch-model.md) for what happens if you register a second
one, or none at all.

## Define the message and handler

```csharp
public sealed record CreateOrder(Guid Id) : IRequest;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder>
{
    public Task HandleAsync(CreateOrder request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

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

## Dispatch it

Resolve `ISender` and call `SendAsync`:

```csharp
var sender = serviceProvider.GetRequiredService<ISender>();

await sender.SendAsync(new CreateOrder(Guid.NewGuid()), cancellationToken);

var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()), cancellationToken);
```

## Where to go next

- Need CQRS-flavored contracts (`ICommand`/`IQuery`) instead of/alongside `IRequest`? See
  [Registering handlers](guides/registering-handlers.md).
- Need to log, wrap in a transaction, or otherwise wrap handler execution? See
  [Using decorators](guides/using-decorators.md).
- Need to fan an event out to multiple handlers? See
  [Publishing notifications](guides/publishing-notifications.md).
- Want reflection-free dispatch instead of the default reflection-based sender? See
  [Enabling source generation](guides/enabling-source-generation.md).
