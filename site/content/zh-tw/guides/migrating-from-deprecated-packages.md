---
title: 從已棄用套件遷移
weight: 50
---

有三個套件是已棄用的相容性用殼（shim）。每一個都沒有自己的內容：它們存在的唯一目的，
是讓已經參照它們的專案，透過依賴被合併進去的那個套件繼續正常運作，而且都預計在未來的
主要版本中移除。

| 已棄用套件 | 合併進 | 起始版本 |
|---|---|---|
| `Davish.Sendr.Message` | `Davish.Sendr.Abstractions` | `Davish.Sendr.Abstractions` 3.1.0+ |
| `Davish.Sendr.Notification` | `Davish.Sendr` | — |
| `Davish.Sendr.Notification.Abstractions` | `Davish.Sendr.Abstractions` | — |

`Davish.Sendr.Message` 原本帶著 `ICommand`/`ICommandHandler`/`IQuery`/
`IQueryHandler`。`Davish.Sendr.Notification` 與 `Davish.Sendr.Notification.Abstractions`
原本帶著 `INotification`/`INotificationHandler`/`INotificationDecorator`/`IPublisher`
（以及 `AddSendrNotification`/`AddNotificationHandler` 這組註冊介面）。這些型別在搬移
前後都位於 `Davish.Sendr` 這個命名空間下，所以遷移只是換套件參照，不需要改
`using`/命名空間：

```diff
- <PackageReference Include="Davish.Sendr.Message" Version="2.0.0" />
- <PackageReference Include="Davish.Sendr.Notification" Version="3.0.0" />
+ <PackageReference Include="Davish.Sendr" Version="3.5.0" />
```

```diff
- <PackageReference Include="Davish.Sendr.Notification.Abstractions" Version="4.0.0" />
+ <PackageReference Include="Davish.Sendr.Abstractions" Version="4.0.0" />
```

如果某個專案只需要合約（不需要 DI 實作），請直接參照 `Davish.Sendr.Abstractions`，
而不是 `Davish.Sendr.Notification.Abstractions` 或 `Davish.Sendr.Message`。如果也需要
DI 實作，請直接參照 `Davish.Sendr`，而不是 `Davish.Sendr.Notification`。
`Davish.Sendr` 本身已經依賴 `Davish.Sendr.Abstractions`。

完整套件清單與目前版本，見[套件列表](../reference/packages.md)。
