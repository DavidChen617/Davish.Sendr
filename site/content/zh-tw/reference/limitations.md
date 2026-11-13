---
title: 限制與注意事項
weight: 50
---

這些行為是刻意的設計，但很容易踩到。

## 一個類別實作多種 handler 介面

`[DecorateWith<...>]` 會套用到類別實作的每一個 handler 介面。這個類別處理幾種訊息，generator
就會用每一種訊息需要的 decorator 介面檢查一次。

```csharp
[DecorateWith<AuditCommandDecorator>] // 只實作 ICommandDecorator
public sealed class OrderHandlers : ICommandHandler<PlaceOrder>, IQueryHandler<GetOrder, OrderDto>
{
    // ...
}
```

這會報 `SENDR001`：`'AuditCommandDecorator' does not implement Davish.Sendr.IQueryDecorator,
required to decorate 'OrderHandlers'.`

怎麼做：

- 把類別拆成一個訊息一個 handler，只在需要的 handler 掛 `[DecorateWith<...>]`。
- 或者讓 decorator 實作這個類別需要的每一種 decorator 介面（這個例子是 `ICommandDecorator` 和
  `IQueryDecorator`）。

## 衍生型別的 request 不會派送到 base 的 handler

兩種 sender 都用訊息的執行期型別找 handler，Microsoft.Extensions.DependencyInjection 也不會解析
variant 的 handler 型別。所以 base 型別的 handler 處理不了衍生型別：

```csharp
public record BaseCommand : ICommand;
public sealed record DerivedCommand : BaseCommand;

services.AddCommandHandler<BaseCommand, BaseCommandHandler>();

await sender.SendAsync(new BaseCommand(), ct);    // 由 BaseCommandHandler 處理
await sender.SendAsync(new DerivedCommand(), ct); // InvalidOperationException：DerivedCommand 沒有 handler
```

這是刻意的取捨：用完全相同的型別查表只要讀一次 dictionary，每次送出都不用沿著繼承鏈往上找。

怎麼做：每個會送出的具體型別都註冊一個 handler。多個 handler 可以用共同的 base class 或服務共用邏輯。

## 在測試裡替換 handler

能不能把 handler 換成 test double，要看 sender 怎麼設定。

| Sender | 替換 `I...Handler<...>` 的註冊 | 替換 handler 類別本身的註冊 |
| --- | --- | --- |
| 手動（`AddXxxHandler`） | 有效，但用 `AddXxxHandler` 設定的 decorator 不會套用到替換後的 handler | 沒有作用：註冊的是介面 |
| Generated（`UseGenerators()`） | 沒有作用：generated sender 直接解析 handler 類別 | 有效，handler 的 `[DecorateWith<...>]` decorator 照常執行 |

用 generated sender 時，最簡單的做法是保留真正的 handler，替換它依賴的服務。需要不同的 handler
實例時，替換 handler 類別本身的註冊：

```csharp
services.Replace(ServiceDescriptor.Transient<GetOrderHandler>(_ => new GetOrderHandler(fakeRepository)));
```

實作了 handler 介面的 test double 本身就是一個 handler。它如果宣告在 generator 會掃描的專案裡（呼叫
`UseGenerators()` 的專案，或用 `IncludeAssemblyOf<...>()` 引入的組件），generator 會找到它，並因為
重複而報 `SENDR002`。

之後的版本可能會調整這個行為。
