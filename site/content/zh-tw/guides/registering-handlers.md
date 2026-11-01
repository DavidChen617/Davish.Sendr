---
title: 註冊 handler
weight: 10
---

這篇涵蓋 request、command、query 的手動註冊。Stream 的部分見下方「Stream」小節。若要
改用 source-generated 的方式，見[啟用 source generation](enabling-source-generation.md)。
這幾種訊息之間的關係，背景說明見[分派模型](../concepts/dispatch-model.md)。

## Request

沒有回傳值的 command，用 `IRequest`：

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

Request/response 用 `IRequest<TResponse>`：

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

兩個都註冊好後，透過 `ISender` 分派：

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<CreateOrder, CreateOrderHandler>()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>();

var sender = serviceProvider.GetRequiredService<ISender>();
await sender.SendAsync(new CreateOrder(Guid.NewGuid()), cancellationToken);
var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()), cancellationToken);
```

## Command 與 query（CQRS）

`ICommand`/`ICommand<TResponse>` 與 `IQuery<TResponse>` 是跟 `IRequest` 獨立的合約，有
自己的 handler 介面（`ICommandHandler`、`IQueryHandler`）與註冊方法
（`AddCommandHandler`、`AddQueryHandler`）。當你想在程式碼裡使用明確的 CQRS 語彙，而不
是泛用的 `IRequest` 時，可以用這一組。

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

呼叫端一樣使用 `ISender.SendAsync`，它會依引數的靜態型別選擇 `IRequest`、`ICommand`
或 `IQuery` 的多載，呼叫端不需要追蹤某個訊息屬於哪一種。

## Stream

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

`ISender` 本身也實作了 `IStreamSender`，所以既有的 `ISender` 實例可以直接分派 stream，
不需要另外再解析一次 `IStreamSender`。

## 錯誤情況

- 為同一個 request/response 組合註冊第二個 handler（不論透過 `AddRequestHandler`、
  `AddCommandHandler`、`AddQueryHandler`，或 `AddStreamRequestHandler`），會丟出
  `InvalidOperationException`。
- 分派一個沒有註冊 handler 的 request，會丟出 `InvalidOperationException`，訊息裡會
  指名缺少的那個註冊呼叫。對 stream 來說，這會發生在開始列舉時，而不是呼叫
  `SendStream` 的時候。

確切的錯誤訊息見[例外與診斷](../reference/exceptions-and-diagnostics.md)。
