---
title: 快速上手
weight: 10
---

## 安裝

```bash
dotnet add package Davish.Sendr
```

如果某個 domain/application 組件只需要參照訊息合約（`IRequest`、`IRequestHandler`、
`ISender` 等），而不想連帶依賴 DI 實作，可以改參照合約套件：

```bash
dotnet add package Davish.Sendr.Abstractions
```

`Davish.Sendr` 本身已經依賴 `Davish.Sendr.Abstractions`，所以應用程式的進入點專案只需要
安裝前者即可。

## 註冊 `AddSendr` 與 handler

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<CreateOrder, CreateOrderHandler>()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>();
```

`AddRequestHandler<TRequest, THandler>` 用於沒有回傳值的 request（`IRequest`）；
`AddRequestHandler<TRequest, TResponse, THandler>` 用於會回傳值的 request
（`IRequest<TResponse>`）。每個 request/response 組合最多只能註冊一個 handler。註冊了
第二個、或完全沒有註冊，會發生什麼事，見[分派模型](concepts/dispatch-model.md)。

## 定義訊息與 handler

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

## 發送

解析出 `ISender` 並呼叫 `SendAsync`：

```csharp
var sender = serviceProvider.GetRequiredService<ISender>();

await sender.SendAsync(new CreateOrder(Guid.NewGuid()), cancellationToken);

var order = await sender.SendAsync(new GetOrder(Guid.NewGuid()), cancellationToken);
```

## 接下來可以看

- 需要 CQRS 風格的合約（`ICommand`/`IQuery`），而不是（或搭配）`IRequest`？見
  [註冊 handler](guides/registering-handlers.md)。
- 需要在 handler 執行前後加上記錄、交易等行為？見
  [使用 decorator](guides/using-decorators.md)。
- 需要把一個事件同時發送給多個 handler？見
  [發佈 notification](guides/publishing-notifications.md)。
- 想要 reflection-free 的分派方式而不是預設的反射式 sender？見
  [啟用 source generation](guides/enabling-source-generation.md)。
