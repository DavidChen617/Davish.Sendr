---
title: 發佈 notification
weight: 30
---

Sequence/Parallel 語意與例外聚合規則的背景說明，見
[Notification 模型](../concepts/notification-model.md)。

## 定義 notification 與它的 handler

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

## 註冊：手動

呼叫一次 `AddSendrNotification()`，然後在**同一次** `AddNotificationHandler` 呼叫裡，
把某個 notification 型別的所有 handler 全部註冊好，並分配到 Sequence 和/或 Parallel
群組：

```csharp
builder.Services
    .AddSendrNotification()
    .AddNotificationHandler<OrderPlaced>(x =>
    {
        x.Handler.Sequence.With<ReserveInventoryHandler>();
        x.Handler.Parallel.With<SendConfirmationEmailHandler>();
    });
```

## 發佈

```csharp
var publisher = serviceProvider.GetRequiredService<IPublisher>();

await publisher.PublishAsync(new OrderPlaced(order.Id), cancellationToken);
```

`PublishAsync` 接受非泛型的 `INotification`，所以像是從 outbox 讀回來的一批不同型別的
事件，可以在呼叫端不知道每一個具體 notification 型別的情況下發佈。沒有任何已註冊
handler 的 notification，發佈就是個 no-op。

## 針對某個 handler 項目加上 decorator

每個 handler 項目都可以帶上自己的 decorator pipeline：

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

## 註冊：產生出來的

`Davish.Sendr.Generators` 也會發現 `INotificationHandler<T>` 的實作。多個類別可以處理
同一個 notification，而不會觸發「重複 handler」的診斷（`SENDR002`）。這裡沒有針對每個
handler 個別設定的執行模式屬性，取而代之的是 `RunAs`，它會設定**同一個 notification
的每個 handler**，在整個產生出來的 publisher 裡怎麼執行。預設是 `Sequence`：

```csharp
builder.Services.AddSendrNotification(o => o.UseGenerators());

// 或者，讓同一個 notification 的 handler 改成同時（並行）執行：
builder.Services.AddSendrNotification(o => o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel)));
```

兩種模式都不保證同一個 notification 的多個 handler 之間的相對順序。Decorator 的宣告
方式跟其他 generated 註冊一樣：

```csharp
[DecorateWith<LoggingNotificationDecorator>]
public sealed class ReserveInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken) { /* ... */ }
}
```

跨 assembly 發現的用法跟 `AddSendr` 一樣，只是有自己獨立的 assembly 集合：

```csharp
o.UseGenerators(x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<ApplicationAssemblyMarker>());
```

完整的發現模型見[編譯期發現](../concepts/compile-time-discovery.md)與
[啟用 source generation](enabling-source-generation.md)。

> 手動的 `AddNotificationHandler` 與產生出來的註冊，對同一個 `IPublisher` 而言是互斥
> 的：一旦 `UseGenerators()` 安裝了產生出來的 publisher，它就只會分派給編譯期發現的
> handler。
