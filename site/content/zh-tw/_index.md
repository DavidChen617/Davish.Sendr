---
title: Davish.Sendr
---

Davish.Sendr 是一個 .NET 的 mediator（中介者）函式庫。它會將 request/response 訊息與
async stream 分派給恰好一個 handler，將 notification 發佈給任意數量的 handler，並可用
decorator pipeline 包裝上述任何一種。Handler 可以手動註冊，也可以透過 Roslyn source
generator 在編譯期自動發現，兩種方式都不會在執行期掃描 assembly。

支援 `netstandard2.0` 與 `net10.0`。

```bash
dotnet add package Davish.Sendr
```

## 從這裡開始

- **[快速上手](getting-started.md)** — 安裝套件並發送第一個 request。
- **概念** — 這個函式庫是怎麼設計的、為什麼這樣設計：
  - [分派模型](concepts/dispatch-model.md) — request、command/query、stream，以及錯誤如何顯現。
  - [Decorator pipeline](concepts/decorator-pipeline.md) — 包裝 handler 的非泛型 pipeline。
  - [Notification 模型](concepts/notification-model.md) — Sequence/Parallel handler 群組與例外聚合規則。
  - [編譯期發現](concepts/compile-time-discovery.md) — `Davish.Sendr.Generators` 做了什麼，以及它與手動註冊的關係。
- **指南** — 針對特定任務的操作說明：
  - [註冊 handler](guides/registering-handlers.md)
  - [使用 decorator](guides/using-decorators.md)
  - [發佈 notification](guides/publishing-notifications.md)
  - [啟用 source generation](guides/enabling-source-generation.md)
  - [從已棄用套件遷移](guides/migrating-from-deprecated-packages.md)
- **參考文件**：
  - [套件列表](reference/packages.md) — 這個 repo 產出的每一個套件，以及該用哪一個。
  - [例外與診斷](reference/exceptions-and-diagnostics.md) — 執行期例外與 `SENDR0xx` 編譯期診斷。
  - [擴充性](reference/extensibility.md) — 如何自行接上自訂的 `ISender`、`IStreamSender` 或 `IPublisher`。

## 套件一覽

| 套件                        | 用途                                                                                                                       |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `Davish.Sendr`              | Request/response 與 stream 分派、notification 發佈、decorator pipeline。                                                   |
| `Davish.Sendr.Abstractions` | 只有合約（contracts）：`IRequest`、`ICommand`、`IQuery`、`INotification`、`ISender` 等，讓 domain 組件不需要依賴 DI 實作。 |
| `Davish.Sendr.Generators`   | 選用的 Roslyn source generator，用於編譯期 handler 發現與 reflection-free 分派。                                           |

`Davish.Sendr.Message`、`Davish.Sendr.Notification`、`Davish.Sendr.Notification.Abstractions`
是已棄用的相容性套件，請見
[從已棄用套件遷移](guides/migrating-from-deprecated-packages.md)以及完整的
[套件參考](reference/packages.md)。
