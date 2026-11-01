---
title: Tracing spans and tags
weight: 40
---

Every span comes from the `ActivitySource` named `Davish.Sendr`
(`SendrActivitySource.Name`) and has `ActivityKind.Internal`. How to subscribe to it is in
[Tracing](../guides/tracing.md).

## Spans

| Span | Display name | Emitted for |
|---|---|---|
| Send | `Send <TypeName>` | Each `ISender.SendAsync` call for a request, command, or query. |
| Publish | `Publish <TypeName>` | Each `IPublisher.PublishAsync` call whose notification has at least one handler. |
| Handle | `Handle <HandlerTypeName>` | Each notification handler that runs, including its decorators. A child of the `Publish` span. |

`<TypeName>` and `<HandlerTypeName>` are the type's short name without its namespace. The
full name is in a tag.

`ISender.SendStream` does not emit a span yet.

## Tags

| Tag | On | Value |
|---|---|---|
| `sendr.kind` | All spans | `request`, `command`, `query`, `notification`, or `notification.handler`. |
| `sendr.request.type` | Send, Publish | Full name of the request, command, query, or notification type. |
| `sendr.handler.type` | Handle | Full name of the handler type. |
| `sendr.notification.group` | Handle | `sequence` or `parallel`. |
| `error.type` | Any span | Full name of the exception type. Set only when the dispatch failed. |

Request and notification contents are never recorded.

## Errors

A span is marked with `ActivityStatusCode.Error` and gets the `error.type` tag when the work
it covers throws. Cancellation (`OperationCanceledException`) is recorded the same way as any
other exception.

For a notification:

- A failed handler marks only its own `Handle` span.
- The `Publish` span is an error when any handler failed. Its `error.type` is the exception
  that `PublishAsync` throws: the exception itself for a single failure, or
  `System.AggregateException` when several handlers failed. See
  [Exceptions and diagnostics](exceptions-and-diagnostics.md).
- A failing handler does not stop the others from running, so their `Handle` spans are still
  emitted.

A span that finishes without an exception keeps the default `Unset` status.

## When no span is created

- No listener is subscribed to `Davish.Sendr`.
- Every subscribed listener's sampler declines the span.
- A notification has no registered handlers.
