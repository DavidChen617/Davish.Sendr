---
title: Tracing
weight: 60
---

Sendr 會為它分派的每一個 request、command、query 與 notification 送出
[`Activity`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activity) span。
這些 span 都來自同一個名為 `Davish.Sendr` 的 `ActivitySource`，所以任何能依名稱訂閱
`ActivitySource` 的 listener（OpenTelemetry 的 tracer provider，或你自己寫的
`ActivityListener`）都能收集到它們。

在有 listener 訂閱之前，不會送出任何東西。`AddSendr()`、`AddSendrNotification()` 與
`UseGenerators()` 都沒有需要另外打開的開關。

span 的確切名稱與 tag 列在
[Tracing 的 span 與 tag](../reference/tracing-spans-and-tags.md)。

## 用 OpenTelemetry 訂閱

把 source 名稱加進你的 tracer provider。這個範例使用 console exporter
（`OpenTelemetry.Exporter.Console`）：

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;

using var tracer = Sdk.CreateTracerProviderBuilder()
    .AddSource(SendrActivitySource.Name) // "Davish.Sendr"
    .AddConsoleExporter()
    .Build();
```

`SendrActivitySource.Name` 是公開常數，所以不需要自己重複寫這個字串。

## 不用 OpenTelemetry 訂閱

單純的 `ActivityListener` 也一樣能運作：

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

## 你會看到什麼

span 會巢狀在你呼叫 `SendAsync` 或 `PublishAsync` 當下的 `Activity` 之下，例如一個
ASP.NET Core 請求。一個會發佈 notification 的 query handler，會產生這樣的樹：

```text
Send SomeQuery
└─ Publish SomeNotification
   └─ Handle SomeNotificationHandler
```

- `Send` 涵蓋 handler 與它的 decorator。
- `Publish` 底下，每個 handler 各有一個 `Handle` 子 span。每個 `Handle` span 也涵蓋該
  handler 自己的 decorator。
- Sequence 群組的 handler 是 `Publish` 底下的同層 span，彼此不會互相巢狀。Parallel
  群組的 handler 會同時執行，所以它們的 span 在時間上會重疊。
- 沒有註冊任何 handler 的 notification，不會產生 span。

分派失敗時，該 span 會被標成錯誤並設定 `error.type` tag。規則見
[Tracing 的 span 與 tag](../reference/tracing-spans-and-tags.md)。

## 在 decorator 裡加上你自己的 span 或 tag

decorator 執行期間，`Activity.Current` 就是外層的 `Send` 或 `Handle` span。decorator
可以對它加 tag，或用你自己擁有的 `ActivitySource` 開一個子 span。Sendr 本身不會為每個
decorator 各送出一個 span。

## 搭配 source generator

產生出來的 sender 與 publisher 會送出相同的 span。產生出來的程式碼會呼叫
`SendrActivitySource`，所以使用 `UseGenerators()` 的專案，執行期需要
`Davish.Sendr` 3.6.0 或更新的版本。只把 `Davish.Sendr.Generators` 升到 2.2.0 以上、
卻沒有升級 `Davish.Sendr`，會編譯失敗。見
[啟用 source generation](enabling-source-generation.md)。

## 限制

- Stream request（`SendStream`）目前還不會被追蹤。
- Sendr 只送出 span，不送出 metrics。
- 有 listener 訂閱期間，handler 同步丟出的例外，會透過回傳的 `Task` 傳給呼叫端，而不是
  在方法回傳之前就丟出。對 `await` 這個呼叫的程式碼來說沒有差別。見
  [例外與診斷](../reference/exceptions-and-diagnostics.md)。

## 輸出範例

[`ConsoleApp.OpenTelemetry` 範例專案](https://github.com/DavidChen617/Davish.Sendr/tree/main/samples/ConsoleApp.OpenTelemetry)
會送出一個 query，而這個 query 的 handler 會發佈一個 notification。搭配上面的 console
exporter，執行一次會印出下面的內容。`Begin Do Stuff`、`SomeNotificationHandler Do stuff`
與 `End Do Stuff` 這三行是範例自己的 decorator 與 handler 印的，不是 Sendr 印的。

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
