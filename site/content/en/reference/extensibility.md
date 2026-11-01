---
title: Extensibility
weight: 30
---

For plugging in a custom `ISender`/`IStreamSender` or `IPublisher` implementation instead of
the default reflection-based one or the generated one.

## `SendrOptions`

Passed to the `configure` delegate of `AddSendr(configure)`.

| Member | Signature | Notes |
|---|---|---|
| `Services` | `IServiceCollection Services { get; }` | The collection being configured by `AddSendr`. An extension (such as the generated `UseGenerators()`) uses this to register handlers/decorators alongside a custom sender. |
| `UseSender<TSender>()` | `SendrOptions UseSender<TSender>() where TSender : class, ISender, IStreamSender` | Registers `TSender` as both `ISender` and `IStreamSender`, replacing the default sender. Returns `this` for chaining. This is what `UseGenerators()` calls internally to install the generated sender. |

`TSender` is resolved as the same instance for both `ISender` and `IStreamSender` within a
scope (`IStreamSender` resolves through `ISender` rather than constructing its own
instance), but each interface is still its own DI registration. If `TSender` is
`IDisposable`/`IAsyncDisposable`, a scope that resolves both interfaces causes the container
to invoke disposal once per registration (twice total) — implement disposal idempotently.

## `NotificationOptions`

Passed to the `configure` delegate of `AddSendrNotification(configure)`.

| Member | Signature | Notes |
|---|---|---|
| `Services` | `IServiceCollection Services { get; }` | The collection being configured by `AddSendrNotification`. |
| `UsePublisher<TPublisher>()` | `NotificationOptions UsePublisher<TPublisher>() where TPublisher : class, IPublisher` | Registers `TPublisher` as `IPublisher`, replacing the default publisher. Returns `this` for chaining. This is what `UseGenerators()` calls internally to install the generated publisher. |

Unlike `UseSender`, there's only the one public interface (`IPublisher`) here, so a custom
`TPublisher` is disposed exactly once by the container, not twice.

## `NotificationGroupRunner`

A public static class with the Sequence/Parallel execution and exception-aggregation
primitives described in [Notification model](../concepts/notification-model.md). Both the
manual `AddNotificationHandler` dispatch path and the generator-emitted code call into it, so
a custom `IPublisher` implementation can reuse the same semantics instead of reimplementing
them.

| Member | Behavior |
|---|---|
| `RunSequenceAsync(steps, cancellationToken)` | Runs each step in order, one at a time; a failing step does not stop the rest from running. Collects every exception. |
| `RunParallelAsync(steps, cancellationToken)` | Starts every step before awaiting any of them; a step that throws synchronously does not stop the others from starting. Collects every exception. |
| `RunBothAsync(sequenceSteps, parallelSteps, cancellationToken)` | Runs a Sequence group and a Parallel group concurrently with each other and combines their exceptions into one result. |

All three follow the same aggregation contract: zero exceptions is silent, exactly one is
rethrown as itself with its original stack trace, and two or more are combined into one
`AggregateException`.
