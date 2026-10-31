---
title: 編譯期發現
weight: 40
---

`Davish.Sendr.Generators` 是一個 Roslyn incremental source generator。它會在編譯期發現
handler 的實作，並產生出 reflection-free 的 `ISender`/`IStreamSender`（以及另外，給
notification 用的 reflection-free `IPublisher`），同時避開執行期 assembly 掃描與
反射式呼叫這兩件事。手動註冊（見[分派模型](dispatch-model.md)）雖然也不會掃描
assembly，但在分派時仍是以反射方式解析並呼叫 handler；source generator 進一步把這個
反射也拿掉了。

## 如何啟用

`AddSendr(o => o.UseGenerators())` 會安裝產生出來的 sender；單純呼叫 `AddSendr()` 則
維持預設的反射式 sender。這兩者互斥，一旦 `UseGenerators()` 生效，手動呼叫的
`AddRequestHandler`/`AddCommandHandler`/`AddQueryHandler`/`AddStreamRequestHandler` 就不會
擴充產生出來的分派表。Notification 這邊也是同樣的關係，獨立運作：透過
`AddSendrNotification(o => o.UseGenerators())`。

分派表是以訊息型別、以及（若適用）回應型別為 key 產生的字典。

## 發現的範圍

預設情況下，source generator 只會發現呼叫 `UseGenerators()` 那個專案自己宣告的
handler。如果 handler 位於某個被參照的專案或 NuGet 套件裡，需要明確宣告：

```csharp
o.UseGenerators(g => g.IncludeAssemblyOf<ApplicationAssemblyMarker>());
```

`ApplicationAssemblyMarker` 可以是那個 assembly 裡任何一個型別，它不需要實作任何特定
介面，只是用來識別要納入的 assembly。`IncludeAssemblyOf<TMarker>()` 可以串接，納入不只
一個 assembly。這裡的「發現」只包含以這種方式指名的 assembly，不包含它們的間接依賴。
目前這個專案永遠會自動被包含，所以再指名自己的 assembly 一次並不會出錯。

Sender/stream-sender 的分派表，跟 publisher 的分派表是各自獨立產生的，各自來自各自的
`UseGenerators()` 呼叫：設定給 `AddSendr(...)` 的 assembly 集合，跟設定給
`AddSendrNotification(...)` 的 assembly 集合彼此無關；同一個函式庫如果同時有兩種
handler，也可以同時被納入兩邊。

這是編譯期的原始碼宣告，不是執行期掃描：source generator 是直接從語法樹讀出
`IncludeAssemblyOf<TMarker>()` 這些呼叫的，所以它們必須直接寫在 `UseGenerators(g =>
...)` 這個 lambda 裡面，不論是串接寫法還是區塊內的個別陳述式都可以。如果放在
delegate 變數、method group、迴圈、條件式，或包一層 helper/extension method 裡，
source generator 讀不出來，會在編譯期被拒絕（`SENDR005`）。

## 什麼情況會被拒絕，以及為什麼

- 宣告成 `struct`/`record struct` 的 handler 是編譯錯誤（`SENDR004`）：不論是產生出來
  還是手動的 handler 解析，都要求是參考型別（reference type）。
- Open generic 的 handler 類別不會被發現（`SENDR003`，資訊等級）。請改用手動註冊，
  註冊一個封閉建構（closed constructed）的 handler 型別。
- 透過 `IncludeAssemblyOf` 發現的 handler，必須是呼叫端專案可以存取到的：public
  （包含每一層外層型別，如果是巢狀 handler 的話），或是 internal 但搭配該函式庫宣告的
  `[assembly: InternalsVisibleTo("...")]`。否則會是編譯錯誤（`SENDR007`），而不是悄悄
  地變成看不見的 handler。
- 同一個 request/response 組合有超過一個 handler 是編譯錯誤（`SENDR002`），不論這些
  handler 是本地的、來自某個被納入的 assembly，還是分散在多個 assembly 裡。這條規則
  不適用於 notification handler，因為本來就允許任意數量的類別處理同一個
  notification 型別。
- 同一次編譯裡，多次設定同一個 sender 或 publisher 的 `UseGenerators()` 呼叫，必須
  宣告完全相同的 assembly 集合；不一致就是編譯錯誤（`SENDR006`）。

完整的診斷清單見[例外與診斷](../reference/exceptions-and-diagnostics.md)，設定步驟見
[啟用 source generation](../guides/enabling-source-generation.md)。

## Decorator 在編譯期就會被驗證

Handler 類別上的 `[DecorateWith<T1, ..., T8>]` 會依照該 handler 所屬的訊息種類所需的
decorator 介面（`IRequestDecorator`、`ICommandDecorator`、`IQueryDecorator`、
`IStreamRequestDecorator`，或 `INotificationDecorator`）在編譯期做檢查（見
[Decorator pipeline](decorator-pipeline.md)）。用法不對就是 `SENDR001`。

## 與手動分派相同的執行語意

產生出來的分派會重用跟手動註冊一樣的 `NotificationGroupRunner` 原語（見
[Notification 模型](notification-model.md)），用於 Sequence/Parallel 執行與例外聚合，
所以兩種註冊方式的行為完全一致，差異只在於 handler 是怎麼被發現與呼叫的。
