---
title: Packages
weight: 10
---

Every package this repository produces, its purpose, and its current version (from each
project's `.csproj`; check the `.csproj` for the current value, since this table is a
snapshot).

| PackageId | Version | Status | Purpose |
|---|---|---|---|
| `Davish.Sendr` | 3.6.0 | Current | Request/response and stream dispatch, notification publishing, decorator pipeline, tracing (`SendrActivitySource`), DI registration (`AddSendr`, `AddSendrNotification`). Depends on `Davish.Sendr.Abstractions`. |
| `Davish.Sendr.Abstractions` | 4.0.0 | Current | Contracts only: `IRequest`, `IRequestHandler`, `IRequestDecorator`, `ISender`, `ICommand`, `IQuery`, `INotification`, `INotificationHandler`, `INotificationDecorator`, `IPublisher`, and related types. No DI implementation. |
| `Davish.Sendr.Generators` | 2.2.0 | Current, opt-in | Roslyn incremental source generator: compile-time handler discovery, `[DecorateWith<...>]` wiring, reflection-free `ISender`/`IStreamSender`/`IPublisher` via `UseGenerators()`. |
| `Davish.Sendr.Message` | 2.0.0 | Deprecated | Compatibility shim for `ICommand`/`ICommandHandler`/`IQuery`/`IQueryHandler`, now in `Davish.Sendr.Abstractions`. No content of its own; will be removed in a future major version. |
| `Davish.Sendr.Notification` | 3.0.0 | Deprecated | Compatibility shim for notification publishing (`IPublisher`, `AddSendrNotification`, `AddNotificationHandler`, …), now in `Davish.Sendr`. No content of its own; will be removed in a future major version. |
| `Davish.Sendr.Notification.Abstractions` | 4.0.0 | Deprecated | Compatibility shim for `INotification`/`INotificationHandler`/`INotificationDecorator`/`IPublisher` contracts, now in `Davish.Sendr.Abstractions`. No content of its own; will be removed in a future major version. |

See [Migrating from deprecated packages](../guides/migrating-from-deprecated-packages.md)
for moving off the deprecated ones.

## Non-published project

`Sendr.Shared` (project `PackageId` falls back to its project name; its `AssemblyName` is
`Davish.Sendr.Shared`) has `IsPackable=false` — it is not distributed as its own NuGet
package. It holds source consumed directly by other projects in this repository, including
the `DecorateWithAttribute<...>` family used by `[DecorateWith<...>]`.

## Shared build settings

All packable projects target `netstandard2.0;net10.0` (`Directory.Build.props`), have
`GenerateDocumentationFile` enabled, and are licensed MIT.
