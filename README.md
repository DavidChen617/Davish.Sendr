# Davish.Sendr

A free, lightweight mediator for .NET — request/response dispatching with a decorator pipeline.

## Install

```bash
dotnet add package Davish.Sendr
```

## Usage

Register the sender and your handlers:

```csharp
builder.Services.AddSendr();

builder.Services
    .AddRequestHandler<SomeRequest, SomeResponse, SomeRequestHandler>(x =>
        x.UseDecorators(typeof(LoggingHandler<,>)));
```

Define a request, its response, and a handler:

```csharp
public record SomeRequest(int Value) : IRequest<SomeResponse>;
public record SomeResponse(int Value1, int Value2);

public sealed class SomeRequestHandler : IRequestHandler<SomeRequest, SomeResponse>
{
    public async Task<SomeResponse> HandleAsync(
        SomeRequest request, CancellationToken cancellationToken = default)
    {
        // ...
        return new SomeResponse(1, 2);
    }
}
```

Send a request:

```csharp
var sender = app.Services.GetRequiredService<ISender>();
var response = await sender.SendAsync(new SomeRequest(1));
```

## Decorators

Wrap handlers with cross-cutting behaviour (logging, validation, transactions…) by
implementing `IRequestDecorator<TRequest, TResponse>` and registering it via `UseDecorators`:

```csharp
public sealed class LoggingHandler<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner,
    ILogger<LoggingHandler<TRequest, TResponse>> logger)
    : IRequestDecorator<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Executing {Request}", typeof(TRequest).Name);
        var result = await inner.HandleAsync(request, cancellationToken);
        logger.LogInformation("Executed {Request}", typeof(TRequest).Name);
        return result;
    }
}
```

## License

MIT © David Chen
