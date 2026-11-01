---
title: Compile-time discovery
weight: 40
---

`Davish.Sendr.Generators` is a Roslyn incremental source generator. It discovers handler
implementations at compile time and emits a reflection-free `ISender`/`IStreamSender`
(and, separately, a reflection-free `IPublisher` for notifications), avoiding both runtime
assembly scanning and reflection-based invocation. Manual registration (see
[Dispatch model](dispatch-model.md)) never scans assemblies either, but still resolves and
invokes handlers reflectively at dispatch time; the generator removes that reflection too.

## Opting in

`AddSendr(o => o.UseGenerators())` installs the generated sender; `AddSendr()` alone keeps
the default reflection-based one. The two are mutually exclusive per application — once
`UseGenerators()` is active, manual `AddRequestHandler`/`AddCommandHandler`/`AddQueryHandler`/
`AddStreamRequestHandler` calls do not extend the generated dispatch table. The same relationship
holds independently for notifications, via `AddSendrNotification(o => o.UseGenerators())`.

Dispatch tables are generated as dictionaries keyed by message type and, where applicable,
response type.

## Discovery scope

By default the generator only discovers handlers declared in the project that calls
`UseGenerators()`. A handler living in a referenced project or NuGet package needs an
explicit declaration:

```csharp
o.UseGenerators(g => g.IncludeAssemblyOf<ApplicationAssemblyMarker>());
```

`ApplicationAssemblyMarker` can be any type declared in that assembly — it doesn't need to
implement anything in particular; it's only used to identify the assembly to include.
`IncludeAssemblyOf<TMarker>()` chains to include more than one assembly. Discovery includes
only the assemblies named this way, not their transitive dependencies. The current project
is always included automatically, so naming its own assembly again is harmless.

The sender/stream-sender dispatch tables and the publisher's dispatch table are generated
independently, each from its own `UseGenerators()` call — the assembly set configured for
`AddSendr(...)` is unrelated to the one configured for `AddSendrNotification(...)`, and the
same library can be included on both sides if it contains both kinds of handlers.

This is a compile-time source declaration, not a runtime scan: the generator reads
`IncludeAssemblyOf<TMarker>()` calls directly out of the syntax tree, so they must be written
inline in the `UseGenerators(g => ...)` lambda — as a fluent chain or as individual
statements in a block. A delegate variable, method group, loop, conditional, or a helper/
extension method that wraps the configuration is not something the generator can read, and
is rejected at compile time (`SENDR005`).

## What gets rejected, and why

- A handler declared as `struct`/`record struct` is a compile error (`SENDR004`): handler
  resolution, generated or manual, requires a reference type.
- An open generic handler class is not discovered (`SENDR003`, informational) — register a
  closed constructed handler type manually instead.
- A handler discovered via `IncludeAssemblyOf` must be accessible from the calling project —
  public (including every containing type, for a nested handler) or internal with a matching
  `[assembly: InternalsVisibleTo("...")]` in the library. Otherwise it's a compile error
  (`SENDR007`) instead of silently being invisible.
- More than one handler for the same request/response pair is a compile error (`SENDR002`),
  whether the handlers are local, in an included assembly, or spread across assemblies. This
  does not apply to notification handlers, since any number of classes may legitimately
  handle the same notification type.
- Multiple `UseGenerators()` calls configuring the same sender or publisher in one
  compilation must declare identical assembly sets; a mismatch is a compile error
  (`SENDR006`).

See [Exceptions and diagnostics](../reference/exceptions-and-diagnostics.md) for the full
diagnostic table, and [Enabling source generation](../guides/enabling-source-generation.md)
for setup steps.

## Decorators are validated at compile time

`[DecorateWith<T1, ..., T8>]` on a handler class is checked against the decorator interface
required for that handler's message kind (`IRequestDecorator`, `ICommandDecorator`,
`IQueryDecorator`, `IStreamRequestDecorator`, or `INotificationDecorator`) — see
[Decorator pipeline](decorator-pipeline.md). Invalid usage is `SENDR001`.

## Same execution semantics as manual dispatch

Generated dispatch reuses the same `NotificationGroupRunner` primitives as manual
registration (see [Notification model](notification-model.md)) for Sequence/Parallel
execution and exception aggregation, so behavior is identical between the two registration
styles — only how handlers are discovered and invoked differs.
