---
title: Migrating from deprecated packages
weight: 50
---

Three packages are deprecated compatibility shims. Each has no content of its own — it
exists only so that a project already referencing it keeps resolving, via a dependency on
the package it was merged into — and each is slated for removal in a future major version.

| Deprecated package | Merged into | Since |
|---|---|---|
| `Davish.Sendr.Message` | `Davish.Sendr.Abstractions` | `Davish.Sendr.Abstractions` 3.1.0+ |
| `Davish.Sendr.Notification` | `Davish.Sendr` | — |
| `Davish.Sendr.Notification.Abstractions` | `Davish.Sendr.Abstractions` | — |

`Davish.Sendr.Message` carried `ICommand`/`ICommandHandler`/`IQuery`/`IQueryHandler`.
`Davish.Sendr.Notification` and `Davish.Sendr.Notification.Abstractions` carried
`INotification`/`INotificationHandler`/`INotificationDecorator`/`IPublisher` (and the
`AddSendrNotification`/`AddNotificationHandler` registration surface). All of these types
live in the `Davish.Sendr` namespace both before and after the move, so migrating is a
package-reference change, not a `using`/namespace change:

```diff
- <PackageReference Include="Davish.Sendr.Message" Version="2.0.0" />
- <PackageReference Include="Davish.Sendr.Notification" Version="3.0.0" />
+ <PackageReference Include="Davish.Sendr" Version="3.5.0" />
```

```diff
- <PackageReference Include="Davish.Sendr.Notification.Abstractions" Version="4.0.0" />
+ <PackageReference Include="Davish.Sendr.Abstractions" Version="4.0.0" />
```

If a project only needs the contracts (no DI implementation), reference
`Davish.Sendr.Abstractions` directly instead of `Davish.Sendr.Notification.Abstractions` or
`Davish.Sendr.Message`. If it needs the DI implementation too, reference `Davish.Sendr`
directly instead of `Davish.Sendr.Notification` — `Davish.Sendr` already depends on
`Davish.Sendr.Abstractions`.

See [Packages](../reference/packages.md) for the full package list and current versions.
