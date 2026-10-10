using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

using TracerProvider tracer = Sdk.CreateTracerProviderBuilder()
      .AddSource(SendrActivitySource.Name)
      .AddConsoleExporter()
      .Build();
/*
or
ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = s => s.Name == SendrActivitySource.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a => Console.WriteLine(
        $"{a.DisplayName} | {a.Duration.TotalMilliseconds:F2} ms | " +
        $"kind={a.GetTagItem("sendr.kind")} | type={a.GetTagItem("sendr.request.type")} | status={a.Status}"),
});
*/

IServiceCollection services = new ServiceCollection();

services
    .AddSendr(o => o.UseGenerators())
    .AddSendrNotification(o => o.UseGenerators());

IServiceProvider sp = services.BuildServiceProvider();

ISender sender = sp.GetRequiredService<ISender>();

await sender.SendAsync(new SomeQuery(), CancellationToken.None);

/* 
    Output:

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
        Schema URL: https://opentelemetry.io/schemas/1.44.0/
*/

record SomeQuery() : IQuery<SomeDto>;

record SomeDto();


[DecorateWith<SomeQueryDecorator>]
class SomeQueryHandler(IPublisher publisher) : IQueryHandler<SomeQuery, SomeDto>
{
    public async Task<SomeDto> HandleAsync(SomeQuery query, CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(new SomeNotification(), cancellationToken);
        return new SomeDto();
    }
}

sealed class SomeQueryDecorator : IQueryDecorator
{
    public async Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) where TQuery : IQuery<TResponse>
    {
        Console.WriteLine("Begin do stuff");
        var r = await next();
        Console.WriteLine("End do stuff");
        return r;
    }
}

record SomeNotification : INotification;

sealed class SomeNotificationHandler : INotificationHandler<SomeNotification>
{
    public Task HandleAsync(SomeNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine("SomeNotificationHandler Do stuff");
        return Task.CompletedTask;
    }
}

