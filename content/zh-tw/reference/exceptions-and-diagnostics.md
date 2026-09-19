---
title: 例外與診斷
weight: 20
---

## 執行期例外

### 找不到 handler — `InvalidOperationException`

當分派一個沒有註冊 handler 的 request/command/query/stream 時丟出（對 stream 來說，
是在開始列舉時）。訊息裡會指名缺少的那個具體註冊呼叫：

```text
Davish.Sendr: no handler is registered for '<requestType>'. Call services.<registrationMethod><<requestType>, YourHandler>() during startup.
```

或者，對有回應型別的 request/command/query 而言：

```text
Davish.Sendr: no handler is registered for '<requestType>' with response '<responseType>'. Call services.<registrationMethod><<requestType>, <responseType>, YourHandler>() during startup.
```

`<registrationMethod>` 會是 `AddRequestHandler`/`AddCommandHandler`/
`AddQueryHandler`/`AddStreamRequestHandler` 裡，符合該訊息種類的那一個。

### 重複註冊 — `InvalidOperationException`

當透過 `AddRequestHandler`、`AddCommandHandler`、`AddQueryHandler`，或
`AddStreamRequestHandler` 為同一個 request/response 組合註冊第二個 handler 時丟出；
另外，當同一個 notification 型別的 `AddNotificationHandler` 被呼叫超過一次時，也會丟出。

### Notification 發佈失敗

`IPublisher.PublishAsync`（不論手動或產生出來的）會聚合每一個實際執行過的 handler 的
失敗，而不是只顯示第一個：

| 兩個群組加起來的失敗數 | 結果 |
|---|---|
| 0 | 不會丟出例外。 |
| 1 | 該例外會原樣重新丟出，並保留原始的堆疊追蹤。 |
| 2 以上 | 會合併成一個包含全部例外的 `AggregateException`。 |

背後的執行語意見[Notification 模型](../concepts/notification-model.md)。

## 編譯期診斷（`Davish.Sendr.Generators`）

| ID | 嚴重性 | 標題 | 什麼時候會發生 |
|---|---|---|---|
| `SENDR001` | Error | Invalid `[DecorateWith]` usage | `[DecorateWith<...>]` 的某個型別參數，沒有實作該 handler 所屬的訊息種類所需的 decorator 介面。 |
| `SENDR002` | Error | Multiple handlers registered for the same request | 有超過一個被發現的 handler（本地或來自某個被納入的 assembly）指向同一個 request/response 組合。不適用於 notification handler。 |
| `SENDR003` | Info | Open generic handler is not discovered | 發現了一個 open generic 的 handler 類別並跳過；請改用手動註冊一個封閉建構的型別。 |
| `SENDR004` | Error | Value type cannot be a Davish.Sendr handler | 某個 handler 宣告成 `struct`/`record struct`。Handler 解析要求是參考型別，請改用 `class`/`record class`。 |
| `SENDR005` | Error | Unsupported `UseGenerators()` configuration | 某個 `IncludeAssemblyOf<...>()` 呼叫沒有直接寫在 `UseGenerators(g => ...)` 的 lambda 裡（例如放在 delegate 變數、method group、迴圈、條件式，或包一層 wrapper helper/extension method），source generator 無法從原始碼讀出它。 |
| `SENDR006` | Error | Inconsistent `UseGenerators()` assembly configuration | 同一次編譯裡，兩個以上針對同一個 sender 或同一個 publisher 的 `UseGenerators()` 呼叫，宣告了不同的 `IncludeAssemblyOf<...>()` 集合。 |
| `SENDR007` | Error | External handler or decorator is not accessible | 透過 `IncludeAssemblyOf` 發現的 handler/decorator，呼叫端專案無法存取（不是 public，也沒有對應的 `[assembly: InternalsVisibleTo("...")]`）。 |

這些診斷所保護的發現模型，見[編譯期發現](../concepts/compile-time-discovery.md)；
設定步驟見[啟用 source generation](../guides/enabling-source-generation.md)。
