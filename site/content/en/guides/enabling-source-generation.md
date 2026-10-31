---
title: Enabling source generation
weight: 40
---

Background on what the generator does and why is in
[Compile-time discovery](../concepts/compile-time-discovery.md).

## Install

```xml
<PackageReference Include="Davish.Sendr" Version="3.5.0" />
<PackageReference Include="Davish.Sendr.Generators" Version="2.1.0" PrivateAssets="all" />
```

`PrivateAssets="all"` keeps the generator itself (an analyzer-style package) from flowing
into downstream consumers of the project.

## Turn it on

```csharp
builder.Services.AddSendr(o => o.UseGenerators());
builder.Services.AddSendrNotification(o => o.UseGenerators());
```

`AddSendr()`/`AddSendrNotification()` alone (without `UseGenerators()`) keep the default
reflection-based sender/publisher. Once `UseGenerators()` is used, manual `AddXHandler`/
`AddNotificationHandler` calls for that same sender/publisher do not extend its dispatch
table — pick one registration style per sender and per publisher.

## Discover handlers in another project

By default, only handlers in the project calling `UseGenerators()` are discovered. To
include a referenced project or package:

```csharp
// Any type declared in the library assembly works as the marker — it doesn't need to
// implement any particular interface.
public sealed class ApplicationAssemblyMarker;

builder.Services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<ApplicationAssemblyMarker>()));
```

Chain it for more than one assembly:

```csharp
o.UseGenerators(g => g
    .IncludeAssemblyOf<ApplicationAssemblyMarker>()
    .IncludeAssemblyOf<InfrastructureAssemblyMarker>());
```

This mirrors a working example in the repository's own sample: `samples/ConsoleApp`
references `samples/ConsoleLib` and includes it via
`IncludeAssemblyOf<IAssemblyMarker>()` for both `AddSendr` and `AddSendrNotification`,
without `ConsoleLib` itself calling `UseGenerators()`.

Write `IncludeAssemblyOf<TMarker>()` calls inline in the `UseGenerators(g => ...)` lambda —
as a fluent chain or as individual statements — since the generator reads this declaration
directly from source rather than scanning at runtime. A delegate variable, method group,
loop, conditional, or helper/extension method wrapping the configuration is rejected at
compile time (`SENDR005`).

A handler discovered this way must be accessible from the calling project: public
(including every containing type, for a nested handler), or internal with a matching
`[assembly: InternalsVisibleTo("...")]` declared by the library — otherwise it's a compile
error (`SENDR007`).

The sender/stream-sender assembly set and the publisher assembly set are independent; the
same library can be included in both if it has both kinds of handlers. Multiple
`UseGenerators()` calls configuring the same sender or publisher must declare identical
assembly sets, or it's a compile error (`SENDR006`).

## Things the generator won't discover

- **Struct/record struct handlers** — compile error `SENDR004`. Use `class`/`record class`.
- **Open generic handler classes** — informational diagnostic `SENDR003`; register a closed
  constructed handler type manually instead, without `UseGenerators()` for that type.

See [Exceptions and diagnostics](../reference/exceptions-and-diagnostics.md) for the
complete diagnostic list.
