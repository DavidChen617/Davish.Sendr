---
title: 擴充性
weight: 30
---

用於接上自訂的 `ISender`/`IStreamSender` 或 `IPublisher` 實作，取代預設的反射式版本
或產生出來的版本。

## `SendrOptions`

傳給 `AddSendr(configure)` 的 `configure` delegate。

| 成員 | 簽章 | 說明 |
|---|---|---|
| `Services` | `IServiceCollection Services { get; }` | `AddSendr` 正在設定的那個 collection。像產生出來的 `UseGenerators()` 這種擴充，會用它來在自訂 sender 之外一併註冊 handler/decorator。 |
| `UseSender<TSender>()` | `SendrOptions UseSender<TSender>() where TSender : class, ISender, IStreamSender` | 把 `TSender` 同時註冊為 `ISender` 與 `IStreamSender`，取代預設的 sender。回傳 `this` 以便串接。這正是 `UseGenerators()` 內部用來安裝產生出來的 sender 的方法。 |

在同一個 scope 內，`TSender` 對 `ISender` 與 `IStreamSender` 會解析成同一個實例
（`IStreamSender` 是透過 `ISender` 解析，而不是自己另外建構一個實例），但每個介面仍是
各自獨立的 DI 註冊。如果 `TSender` 實作了 `IDisposable`/`IAsyncDisposable`，某個 scope
同時解析這兩個介面時，容器會依每個註冊各呼叫一次 disposal（總共兩次），請把 disposal
實作成可重複呼叫（idempotent）。

## `NotificationOptions`

傳給 `AddSendrNotification(configure)` 的 `configure` delegate。

| 成員 | 簽章 | 說明 |
|---|---|---|
| `Services` | `IServiceCollection Services { get; }` | `AddSendrNotification` 正在設定的那個 collection。 |
| `UsePublisher<TPublisher>()` | `NotificationOptions UsePublisher<TPublisher>() where TPublisher : class, IPublisher` | 把 `TPublisher` 註冊為 `IPublisher`，取代預設的 publisher。回傳 `this` 以便串接。這正是 `UseGenerators()` 內部用來安裝產生出來的 publisher 的方法。 |

跟 `UseSender` 不同，這裡只有一個公開介面（`IPublisher`），所以自訂的 `TPublisher`
會被容器剛好 dispose 一次，不會是兩次。

## `NotificationGroupRunner`

一個公開的 static 類別，內含[Notification 模型](../concepts/notification-model.md)裡
描述的 Sequence/Parallel 執行與例外聚合原語。手動的 `AddNotificationHandler` 分派路徑
與 source generator 產生出來的程式碼都會呼叫它，所以自訂的 `IPublisher` 實作可以直接
重用這套語意，而不需要自己重新實作一遍。

| 成員 | 行為 |
|---|---|
| `RunSequenceAsync(steps, cancellationToken)` | 依順序、一次一個地執行每個步驟；某個步驟失敗不會阻止其他步驟繼續執行。收集所有例外。 |
| `RunParallelAsync(steps, cancellationToken)` | 在等待任何一個步驟之前，先把全部步驟都啟動；某個步驟在啟動時就同步丟出例外，也不會阻止其他步驟啟動。收集所有例外。 |
| `RunBothAsync(sequenceSteps, parallelSteps, cancellationToken)` | 讓一個 Sequence 群組與一個 Parallel 群組彼此同時執行，並把兩者的例外合併成一個結果。 |

這三個方法都遵循相同的聚合規則：零個例外時保持沉默，剛好一個例外時原樣重新丟出並保留
原始堆疊追蹤，兩個以上則合併成一個 `AggregateException`。
