---
title: 套件列表
weight: 10
---

這個 repo 產出的每一個套件、它的用途，以及目前版本（來自各專案的 `.csproj`；請以
`.csproj` 目前的值為準，這份表格只是某一刻的快照）。

| PackageId | 版本 | 狀態 | 用途 |
|---|---|---|---|
| `Davish.Sendr` | 3.5.0 | 目前主力 | Request/response 與 stream 分派、notification 發佈、decorator pipeline、DI 註冊（`AddSendr`、`AddSendrNotification`）。依賴 `Davish.Sendr.Abstractions`。 |
| `Davish.Sendr.Abstractions` | 4.0.0 | 目前主力 | 只有合約：`IRequest`、`IRequestHandler`、`IRequestDecorator`、`ISender`、`ICommand`、`IQuery`、`INotification`、`INotificationHandler`、`INotificationDecorator`、`IPublisher` 等相關型別。沒有 DI 實作。 |
| `Davish.Sendr.Generators` | 2.1.0 | 目前主力，選用 | Roslyn incremental source generator：編譯期 handler 發現、`[DecorateWith<...>]` 佈線，透過 `UseGenerators()` 提供 reflection-free 的 `ISender`/`IStreamSender`/`IPublisher`。 |
| `Davish.Sendr.Message` | 2.0.0 | 已棄用 | `ICommand`/`ICommandHandler`/`IQuery`/`IQueryHandler` 的相容性用殼，現在移到 `Davish.Sendr.Abstractions`。沒有自己的內容；未來的主要版本會移除。 |
| `Davish.Sendr.Notification` | 3.0.0 | 已棄用 | Notification 發佈（`IPublisher`、`AddSendrNotification`、`AddNotificationHandler` 等）的相容性用殼，現在移到 `Davish.Sendr`。沒有自己的內容；未來的主要版本會移除。 |
| `Davish.Sendr.Notification.Abstractions` | 4.0.0 | 已棄用 | `INotification`/`INotificationHandler`/`INotificationDecorator`/`IPublisher` 合約的相容性用殼，現在移到 `Davish.Sendr.Abstractions`。沒有自己的內容；未來的主要版本會移除。 |

從已棄用套件遷移的方式，見
[從已棄用套件遷移](../guides/migrating-from-deprecated-packages.md)。

## 不會發行的專案

`Sendr.Shared`（專案的 `PackageId` 會退回用專案名稱；它的 `AssemblyName` 是
`Davish.Sendr.Shared`）設定了 `IsPackable=false`，它不會以自己的 NuGet 套件形式發行。
它保存的是這個 repo 裡其他專案直接參照的原始碼，包括 `[DecorateWith<...>]` 所使用的
`DecorateWithAttribute<...>` 系列。

## 共用的建置設定

所有可打包的專案都以 `netstandard2.0;net10.0`（`Directory.Build.props`）為目標，都啟用
了 `GenerateDocumentationFile`，並採用 MIT 授權。
