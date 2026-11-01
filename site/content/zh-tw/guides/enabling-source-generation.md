---
title: 啟用 source generation
weight: 40
---

Source generator 做了什麼、為什麼要用它，背景說明見
[編譯期發現](../concepts/compile-time-discovery.md)。

## 安裝

```xml
<PackageReference Include="Davish.Sendr" Version="3.5.0" />
<PackageReference Include="Davish.Sendr.Generators" Version="2.1.0" PrivateAssets="all" />
```

`PrivateAssets="all"` 讓這個 generator 本身（一個 analyzer 類型的套件）不會流到下游、
參照這個專案的其他專案。

## 開啟它

```csharp
builder.Services.AddSendr(o => o.UseGenerators());
builder.Services.AddSendrNotification(o => o.UseGenerators());
```

單純呼叫 `AddSendr()`/`AddSendrNotification()`（不帶 `UseGenerators()`）會維持預設的
反射式 sender/publisher。一旦用了 `UseGenerators()`，同一個 sender/publisher 的手動
`AddXHandler`/`AddNotificationHandler` 呼叫就不會擴充它的分派表。每個 sender、每個
publisher 都只能選一種註冊方式。

## 發現另一個專案裡的 handler

預設只會發現呼叫 `UseGenerators()` 那個專案自己的 handler。要納入被參照的專案或套件：

```csharp
// assembly 裡任何一個型別都可以當這個 marker，它不需要實作任何特定介面。
public sealed class ApplicationAssemblyMarker;

builder.Services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<ApplicationAssemblyMarker>()));
```

要納入不只一個 assembly，就串接起來：

```csharp
o.UseGenerators(g => g
    .IncludeAssemblyOf<ApplicationAssemblyMarker>()
    .IncludeAssemblyOf<InfrastructureAssemblyMarker>());
```

這對應到 repo 自己 sample 裡一個實際可運作的範例：`samples/ConsoleApp` 參照了
`samples/ConsoleLib`，並在 `AddSendr` 與 `AddSendrNotification` 兩邊都透過
`IncludeAssemblyOf<IAssemblyMarker>()` 把它納入，而 `ConsoleLib` 本身完全沒有呼叫
`UseGenerators()`。

`IncludeAssemblyOf<TMarker>()` 呼叫要直接寫在 `UseGenerators(g => ...)` 這個
lambda 裡，不論是串接寫法還是區塊內的個別陳述式都行，因為 source generator 是直接從
原始碼讀出這個宣告的，不是在執行期掃描。如果放在 delegate 變數、method group、迴圈、
條件式，或包一層 helper/extension method 裡，會在編譯期被拒絕（`SENDR005`）。

用這種方式發現的 handler，必須是呼叫端專案可以存取到的：public（包含每一層外層型別，
如果是巢狀 handler 的話），或是 internal 但搭配該函式庫宣告的
`[assembly: InternalsVisibleTo("...")]`，否則會是編譯錯誤（`SENDR007`）。

Sender/stream-sender 的 assembly 集合跟 publisher 的 assembly 集合是彼此獨立的；同一個
函式庫如果同時有兩種 handler，也可以同時被納入兩邊。同一次編譯裡，多次設定同一個
sender 或 publisher 的 `UseGenerators()` 呼叫，必須宣告完全相同的 assembly 集合，不然
就是編譯錯誤（`SENDR006`）。

## Source generator 不會發現的情況

- **Struct/record struct 的 handler** — 編譯錯誤 `SENDR004`。請改用 `class`/
  `record class`。
- **Open generic 的 handler 類別** — 資訊等級的診斷 `SENDR003`；請針對這個型別改用
  手動註冊一個封閉建構的 handler 型別，不要對它用 `UseGenerators()`。

完整的診斷清單見[例外與診斷](../reference/exceptions-and-diagnostics.md)。
