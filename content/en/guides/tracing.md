---
title: Tracing
weight: 60
---

Sendr emits [`Activity`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activity)
spans for every request, command, query, and notification it dispatches. The spans come from
one `ActivitySource` named `Davish.Sendr`, so any listener that can subscribe to an
`ActivitySource` by name — OpenTelemetry's tracer provider, or your own `ActivityListener` —
can collect them.

Nothing is emitted until a listener subscribes. There is no switch to turn on in
`AddSendr()`, `AddSendrNotification()`, or `UseGenerators()`.

The exact span names and tags are listed in
[Tracing spans and tags](../reference/tracing-spans-and-tags.md).

## Subscribe with OpenTelemetry

Add the source name to your tracer provider. This example uses the console exporter
(`OpenTelemetry.Exporter.Console`):

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;

using var tracer = Sdk.CreateTracerProviderBuilder()
    .AddSource(SendrActivitySource.Name) // "Davish.Sendr"
    .AddConsoleExporter()
    .Build();
```

`SendrActivitySource.Name` is a public constant, so you do not need to repeat the string.

## Subscribe without OpenTelemetry

A plain `ActivityListener` works the same way:

```csharp
using System.Diagnostics;

ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = source => source.Name == SendrActivitySource.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activity => Console.WriteLine(
        $"{activity.DisplayName} {activity.Duration.TotalMilliseconds:F2} ms"),
});
```

## What you see

Spans nest under whatever `Activity` is current where you call `SendAsync` or
`PublishAsync`, such as an ASP.NET Core request. A query handler that publishes a
notification produces this tree:

```text
Send SomeQuery
└─ Publish SomeNotification
   └─ Handle SomeNotificationHandler
```

- `Send` covers the handler together with its decorators.
- `Publish` has one `Handle` child per handler. Each `Handle` span also covers that
  handler's own decorators.
- Handlers in the Sequence group are siblings under `Publish`, not nested in one another.
  Handlers in the Parallel group run at the same time, so their spans overlap.
- A notification with no registered handlers produces no span.

A failed dispatch marks its span as an error and sets the `error.type` tag. See
[Tracing spans and tags](../reference/tracing-spans-and-tags.md#errors) for the rules.

## Add your own spans or tags inside a decorator

While a decorator runs, `Activity.Current` is the surrounding `Send` or `Handle` span. A
decorator can add a tag to it, or start its own child span from an `ActivitySource` that you
own. Sendr itself does not emit a span per decorator.

## With the source generator

The generated sender and publisher emit the same spans. The generated code calls
`SendrActivitySource`, so a project that uses `UseGenerators()` needs `Davish.Sendr` 3.6.0 or
later at runtime. Upgrading only `Davish.Sendr.Generators` to 2.2.0 or later, without
upgrading `Davish.Sendr`, fails to compile. See
[Enabling source generation](enabling-source-generation.md).

## Limits

- Stream requests (`SendStream`) are not traced yet.
- Sendr emits spans only. It does not emit metrics.
- While a listener is subscribed, an exception that a handler throws synchronously reaches
  the caller through the returned `Task` instead of being thrown before it returns. Code that
  `await`s the call sees no difference. See
  [Exceptions and diagnostics](../reference/exceptions-and-diagnostics.md).

## Example output

The [`ConsoleApp.OpenTelemetry` sample](https://github.com/DavidChen617/Davish.Sendr/tree/main/samples/ConsoleApp.OpenTelemetry)
sends a query whose handler publishes a notification. With the console exporter from above,
one run prints the following. The `Begin Do Stuff`, `SomeNotificationHandler Do stuff`, and
`End Do Stuff` lines come from the sample's own decorator and handler, not from Sendr.

```text
Begin Do Stuff
SomeNotificationHandler Do stuff
Activity.TraceId:            7ec3f8b3a80d748acad31f30b42005a9
Activity.SpanId:             f31278a6f2680ff7
Activity.TraceFlags:         Recorded, RandomTraceId
Activity.ParentSpanId:       c52d8cd7aba96d86
Activity.DisplayName:        Handle SomeNotificationHandler
Activity.Kind:               Internal
Activity.StartTime:          2026-10-10T04:30:51.8997700Z
Activity.Duration:           00:00:00.0000960
Activity.Tags:
    sendr.kind: notification.handler
    sendr.handler.type: SomeNotificationHandler
    sendr.notification.group: sequence
Instrumentation scope (ActivitySource):
    Name: Davish.Sendr
Resource associated with Activity:
    telemetry.sdk.name: opentelemetry
    telemetry.sdk.language: dotnet
    telemetry.sdk.version: 1.19.1
    service.name: unknown_service:ConsoleApp.OpenTelemetry
    Schema URL: https://opentelemetry.io/schemas/1.44.0

Activity.TraceId:            7ec3f8b3a80d748acad31f30b42005a9
Activity.SpanId:             c52d8cd7aba96d86
Activity.TraceFlags:         Recorded, RandomTraceId
Activity.ParentSpanId:       19348e108977f3bd
Activity.DisplayName:        Publish SomeNotification
Activity.Kind:               Internal
Activity.StartTime:          2026-10-10T04:30:51.8988670Z
Activity.Duration:           00:00:00.0010730
Activity.Tags:
    sendr.kind: notification
    sendr.request.type: SomeNotification
Instrumentation scope (ActivitySource):
    Name: Davish.Sendr
Resource associated with Activity:
    telemetry.sdk.name: opentelemetry
    telemetry.sdk.language: dotnet
    telemetry.sdk.version: 1.19.1
    service.name: unknown_service:ConsoleApp.OpenTelemetry
    Schema URL: https://opentelemetry.io/schemas/1.44.0

End Do Stuff
Activity.TraceId:            7ec3f8b3a80d748acad31f30b42005a9
Activity.SpanId:             19348e108977f3bd
Activity.TraceFlags:         Recorded, RandomTraceId
Activity.DisplayName:        Send SomeQuery
Activity.Kind:               Internal
Activity.StartTime:          2026-10-10T04:30:51.8972140Z
Activity.Duration:           00:00:00.0027760
Activity.Tags:
    sendr.kind: query
    sendr.request.type: SomeQuery
Instrumentation scope (ActivitySource):
    Name: Davish.Sendr
Resource associated with Activity:
    telemetry.sdk.name: opentelemetry
    telemetry.sdk.language: dotnet
    telemetry.sdk.version: 1.19.1
    service.name: unknown_service:ConsoleApp.OpenTelemetry
    Schema URL: https://opentelemetry.io/schemas/1.44.0
```
