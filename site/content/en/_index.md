---
title: Davish.Sendr
---


Davish.Sendr is a mediator library for .NET. It dispatches request/response messages and
async streams to exactly one handler each, publishes notifications to any number of
handlers, and wraps any of them with a decorator pipeline. Handlers can be registered
manually or discovered at compile time by a Roslyn source generator; neither path scans
assemblies at runtime.

Targets `netstandard2.0` and `net10.0`.

```bash
dotnet add package Davish.Sendr
```

## Where to start

- **[Getting started](getting-started.md)** — install the package and dispatch your first
  request.
- **Concepts** — how the library is put together and why:
  - [Dispatch model](concepts/dispatch-model.md) — requests, commands/queries, streams, and
    how errors surface.
  - [Decorator pipeline](concepts/decorator-pipeline.md) — the non-generic pipeline that
    wraps handlers.
  - [Notification model](concepts/notification-model.md) — Sequence/Parallel handler groups
    and exception aggregation.
  - [Compile-time discovery](concepts/compile-time-discovery.md) — what
    `Davish.Sendr.Generators` does and how it relates to manual registration.
- **Guides** — task-oriented how-tos:
  - [Registering handlers](guides/registering-handlers.md)
  - [Using decorators](guides/using-decorators.md)
  - [Publishing notifications](guides/publishing-notifications.md)
  - [Enabling source generation](guides/enabling-source-generation.md)
  - [Migrating from deprecated packages](guides/migrating-from-deprecated-packages.md)
- **Reference**:
  - [Packages](reference/packages.md) — every package this repository produces, and which
    one to use.
  - [Exceptions and diagnostics](reference/exceptions-and-diagnostics.md) — runtime
    exceptions and the `SENDR0xx` compiler diagnostics.
  - [Extensibility](reference/extensibility.md) — plugging in a custom `ISender`,
    `IStreamSender`, or `IPublisher`.

## Packages at a glance

| Package | Purpose |
|---|---|
| `Davish.Sendr` | Request/response and stream dispatch, notification publishing, decorator pipeline. |
| `Davish.Sendr.Abstractions` | Contracts only (`IRequest`, `ICommand`, `IQuery`, `INotification`, `ISender`, …) for domain assemblies that shouldn't depend on the DI implementation. |
| `Davish.Sendr.Generators` | Optional Roslyn source generator for compile-time handler discovery and reflection-free dispatch. |

`Davish.Sendr.Message`, `Davish.Sendr.Notification`, and `Davish.Sendr.Notification.Abstractions`
are deprecated compatibility packages — see
[Migrating from deprecated packages](guides/migrating-from-deprecated-packages.md) and the
full [package reference](reference/packages.md).
