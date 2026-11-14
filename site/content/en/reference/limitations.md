---
title: Limitations
weight: 50
---

Behavior that is by design but easy to run into.

## One class, several handler interfaces

`[DecorateWith<...>]` applies to every handler interface the class implements. The generator
checks each decorator against the decorator interface of every message kind the class handles.

```csharp
[DecorateWith<AuditCommandDecorator>] // implements ICommandDecorator only
public sealed class OrderHandlers : ICommandHandler<PlaceOrder>, IQueryHandler<GetOrder, OrderDto>
{
    // ...
}
```

This reports `SENDR001`: `'AuditCommandDecorator' does not implement Davish.Sendr.IQueryDecorator,
required to decorate 'OrderHandlers'.`

What to do:

- Split the class into one handler per message, and put `[DecorateWith<...>]` only on the handlers
  that need it.
- Or use a decorator that implements every decorator interface the class needs (here,
  `ICommandDecorator` and `IQueryDecorator`).

## A derived request is not dispatched to the base handler

Both senders look up the handler by the message's runtime type, and Microsoft.Extensions.DependencyInjection
does not resolve variant handler types. A handler for a base type does not handle a derived type:

```csharp
public record BaseCommand : ICommand;
public sealed record DerivedCommand : BaseCommand;

services.AddCommandHandler<BaseCommand, BaseCommandHandler>();

await sender.SendAsync(new BaseCommand(), ct);    // handled by BaseCommandHandler
await sender.SendAsync(new DerivedCommand(), ct); // InvalidOperationException: no handler for DerivedCommand
```

This is deliberate: a lookup by exact type is a single dictionary read, without walking the
inheritance chain on every send.

What to do: register a handler for each concrete type you send. Several handlers can share their
logic through a common base class or service.

## Replacing a handler in tests

How you can swap a handler for a test double depends on how the sender was set up.

| Sender | Replace the `I...Handler<...>` registration | Replace the handler class's registration |
| --- | --- | --- |
| Manual (`AddXxxHandler`) | Works, but decorators configured through `AddXxxHandler` are not applied to the replacement | Has no effect: the registration is for the interface |
| Generated (`UseGenerators()`) | Has no effect: the generated sender resolves the handler class itself | Works, and the handler's `[DecorateWith<...>]` decorators still run |

With the generated sender, the simplest approach is to keep the real handler and replace what it
depends on. If you need a different handler instance, replace the handler class's registration.
Call `Replace` after `AddSendr`, which is where the handler class is registered:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions; // for Replace

services.AddSendr(o => o.UseGenerators());
services.Replace(ServiceDescriptor.Transient<GetOrderHandler>(_ => new GetOrderHandler(fakeRepository)));
```

A test double that implements a handler interface is itself a handler. If it is declared in a project
that references Davish.Sendr.Generators, and the real handler is discovered by the same generator run
(it is in the same project, or in an assembly that project includes with `IncludeAssemblyOf<...>()`),
the generator reports `SENDR002` for the duplicate.

This may change in a future version.
