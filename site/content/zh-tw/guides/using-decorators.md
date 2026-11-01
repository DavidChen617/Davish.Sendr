---
title: 使用 decorator
weight: 20
---

Pipeline 模型與順序規則的背景說明，見
[Decorator pipeline](../concepts/decorator-pipeline.md)。這篇示範手動與產生出來的兩種
附加方式。

## 手動：`x.Decorator.With<T>()`

在註冊呼叫傳入 `configure` delegate，並依「外到內」的順序串接 `With<T>()`：

```csharp
builder.Services
    .AddSendr()
    .AddRequestHandler<GetOrder, OrderDto, GetOrderHandler>(x => x.Decorator
        .With<TransactionDecorator>()
        .With<LoggingDecorator>());
```

這裡 `TransactionDecorator` 是最外層（最先執行、最後結束）；`LoggingDecorator` 巢狀
包在它裡面。

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

同時實作 `IRequestDecorator` 與 `IRequestDecorator.WithResponse`，讓一個類別可以同時
包裝 `IRequest` 與 `IRequest<TResponse>` 的 handler。同樣的模式也適用於
`ICommandDecorator`/`ICommandDecorator.WithResponse`、`IQueryDecorator`、
`IStreamRequestDecorator`，以及 `INotificationDecorator`（notification 特有的設定方式
見[發佈 notification](publishing-notifications.md)）。

Command 用的是同樣的形狀，只是換成 `ICommandDecorator`：

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

Stream 用的是 `IStreamRequestDecorator`：

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

> 若某個 stream decorator 使用 `yield`，應該把列舉包在 `try`/`finally` 裡，並透過
> `[EnumeratorCancellation]` 把 token 往下傳，這樣 cancellation 與 disposal 才能正確地
> 傳遞下去。

## 產生出來的：`[DecorateWith<...>]`

使用[source-generated 註冊](enabling-source-generation.md)時，直接在 handler 類別上
宣告 decorator。型別參數的順序是外到內，跟等效的 `With<T>()` 串接順序一致：

```csharp
[DecorateWith<TransactionDecorator, LoggingDecorator>]
public sealed class GetOrderHandler : IRequestHandler<GetOrder, OrderDto>
{
    public Task<OrderDto> HandleAsync(GetOrder request, CancellationToken cancellationToken) { /* ... */ }
}
```

`DecorateWith<...>` 有 1 到 8 個型別參數的多個版本；每個 handler 類別最多只能套用一個。
Source generator 會依該 handler 所屬的訊息種類所需的 decorator 介面，驗證屬性上的型別
參數，不符合就在編譯期拒絕（`SENDR001`），詳見
[例外與診斷](../reference/exceptions-and-diagnostics.md)。
