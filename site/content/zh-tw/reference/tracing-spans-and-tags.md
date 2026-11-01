---
title: Tracing 的 span 與 tag
weight: 40
---

每個 span 都來自名為 `Davish.Sendr` 的 `ActivitySource`（`SendrActivitySource.Name`），
`ActivityKind` 是 `Internal`。如何訂閱它，見 [Tracing](../guides/tracing.md)。

## Span

| Span | 顯示名稱 | 什麼時候送出 |
|---|---|---|
| Send | `Send <TypeName>` | 每一次對 request、command 或 query 呼叫 `ISender.SendAsync`。 |
| Publish | `Publish <TypeName>` | 每一次 `IPublisher.PublishAsync`，且該 notification 至少有一個 handler。 |
| Handle | `Handle <HandlerTypeName>` | 每一個實際執行的 notification handler，包含它的 decorator。是 `Publish` span 的子 span。 |

`<TypeName>` 與 `<HandlerTypeName>` 是該型別不含命名空間的簡短名稱。完整名稱放在 tag 裡。

`ISender.SendStream` 目前還不會送出 span。

## Tag

| Tag | 出現在 | 值 |
|---|---|---|
| `sendr.kind` | 所有 span | `request`、`command`、`query`、`notification` 或 `notification.handler`。 |
| `sendr.request.type` | Send、Publish | request、command、query 或 notification 型別的完整名稱。 |
| `sendr.handler.type` | Handle | handler 型別的完整名稱。 |
| `sendr.notification.group` | Handle | `sequence` 或 `parallel`。 |
| `error.type` | 任何 span | 例外型別的完整名稱。只在分派失敗時設定。 |

request 與 notification 的內容絕不會被記錄。

## 錯誤

當 span 所涵蓋的工作丟出例外時，該 span 會被標成 `ActivityStatusCode.Error`，並加上
`error.type` tag。取消（`OperationCanceledException`）的記錄方式與其他例外相同。

對 notification 來說：

- 失敗的 handler 只會把它自己的 `Handle` span 標成錯誤。
- 只要有任何 handler 失敗，`Publish` span 就是錯誤。它的 `error.type` 是
  `PublishAsync` 丟出的例外：單一失敗時是該例外本身，多個 handler 失敗時是
  `System.AggregateException`。見[例外與診斷](exceptions-and-diagnostics.md)。
- 失敗的 handler 不會阻止其他 handler 執行，所以其他 handler 的 `Handle` span 仍然會送出。

沒有丟出例外就結束的 span，維持預設的 `Unset` 狀態。

## 什麼時候不會建立 span

- 沒有任何 listener 訂閱 `Davish.Sendr`。
- 所有已訂閱 listener 的 sampler 都拒絕了這個 span。
- notification 沒有註冊任何 handler。
