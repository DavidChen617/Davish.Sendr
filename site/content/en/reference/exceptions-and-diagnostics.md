---
title: Exceptions and diagnostics
weight: 20
---

## Runtime exceptions

### Missing handler — `InvalidOperationException`

Thrown when dispatching a request/command/query/stream with no registered handler (for
streams, when enumeration starts). The message names the specific registration call that's
missing:

```text
Davish.Sendr: no handler is registered for '<requestType>'. Call services.<registrationMethod><<requestType>, YourHandler>() during startup.
```

or, for a request/command/query with a response type:

```text
Davish.Sendr: no handler is registered for '<requestType>' with response '<responseType>'. Call services.<registrationMethod><<requestType>, <responseType>, YourHandler>() during startup.
```

`<registrationMethod>` is whichever of `AddRequestHandler`/`AddCommandHandler`/
`AddQueryHandler`/`AddStreamRequestHandler` matches the message's kind.

### Duplicate registration — `InvalidOperationException`

Thrown when a second handler is registered for the same request/response pair via
`AddRequestHandler`, `AddCommandHandler`, `AddQueryHandler`, or `AddStreamRequestHandler`;
and separately, when `AddNotificationHandler` is called more than once for the same
notification type.

### Notification publishing failures

`IPublisher.PublishAsync` (manual or generated) aggregates failures from every handler that
ran, rather than surfacing only the first:

| Failures across both groups | Result |
|---|---|
| 0 | No exception. |
| 1 | That exception is rethrown as itself, with its original stack trace. |
| 2+ | Combined into one `AggregateException` containing all of them. |

See [Notification model](../concepts/notification-model.md) for the execution semantics
behind this.

### Synchronous exceptions while a tracing listener is subscribed

When a listener is subscribed to the `Davish.Sendr` `ActivitySource`, an exception that
a handler throws synchronously — before it returns a `Task` — reaches the caller through the
returned `Task` instead of being thrown by `SendAsync`/`PublishAsync` itself. Code that
`await`s the call observes the same exception either way. Without a listener, behavior is
unchanged. See [Tracing](../guides/tracing.md).

## Compile-time diagnostics (`Davish.Sendr.Generators`)

| ID | Severity | Title | When it fires |
|---|---|---|---|
| `SENDR001` | Error | Invalid `[DecorateWith]` usage | A `[DecorateWith<...>]` type argument doesn't implement the decorator interface required for that handler's message kind. |
| `SENDR002` | Error | Multiple handlers registered for the same request | More than one discovered handler (local and/or from an included assembly) targets the same request/response pair. Does not apply to notification handlers. |
| `SENDR003` | Info | Open generic handler is not discovered | An open generic handler class was found and skipped; register a closed constructed type manually instead. |
| `SENDR004` | Error | Value type cannot be a Davish.Sendr handler | A handler is declared as `struct`/`record struct`. Handler resolution requires a reference type — use `class`/`record class`. |
| `SENDR005` | Error | Unsupported `UseGenerators()` configuration | An `IncludeAssemblyOf<...>()` call isn't written inline in the `UseGenerators(g => ...)` lambda (e.g. it's behind a delegate variable, method group, loop, conditional, or wrapping helper/extension method), so the generator can't read it from source. |
| `SENDR006` | Error | Inconsistent `UseGenerators()` assembly configuration | Two or more `UseGenerators()` calls for the same sender or the same publisher in one compilation declare different `IncludeAssemblyOf<...>()` sets. |
| `SENDR007` | Error | External handler or decorator is not accessible | A handler/decorator discovered via `IncludeAssemblyOf` isn't accessible from the calling project (not public, and no matching `[assembly: InternalsVisibleTo("...")]`). |

See [Compile-time discovery](../concepts/compile-time-discovery.md) for the discovery model
these diagnostics protect, and
[Enabling source generation](../guides/enabling-source-generation.md) for setup.
