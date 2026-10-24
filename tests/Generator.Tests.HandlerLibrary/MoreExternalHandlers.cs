using Davish.Sendr;

namespace Generator.Tests.HandlerLibrary;

// One handler per SendAsync overload and SendStream, so the host project's generated sender can be checked
// against every handler kind discovered through IncludeAssemblyOf<HandlerLibraryMarker>().

public sealed record ExternalRequest : IRequest;

public sealed class ExternalRequestHandler(ExternalLogCollector collector) : IRequestHandler<ExternalRequest>
{
    public Task HandleAsync(ExternalRequest request, CancellationToken cancellationToken)
    {
        collector.Log.Add(nameof(ExternalRequestHandler));
        return Task.CompletedTask;
    }
}

public sealed record ExternalRequestForValue : IRequest<string>;

public sealed class ExternalRequestForValueHandler : IRequestHandler<ExternalRequestForValue, string>
{
    public Task<string> HandleAsync(ExternalRequestForValue request, CancellationToken cancellationToken)
        => Task.FromResult(nameof(ExternalRequestForValueHandler));
}

public sealed record ExternalCommandForValue : ICommand<string>;

public sealed class ExternalCommandForValueHandler : ICommandHandler<ExternalCommandForValue, string>
{
    public Task<string> HandleAsync(ExternalCommandForValue command, CancellationToken cancellationToken)
        => Task.FromResult(nameof(ExternalCommandForValueHandler));
}

public sealed record ExternalStream : IStreamRequest<string>;

public sealed class ExternalStreamHandler : IStreamRequestHandler<ExternalStream, string>
{
    public async IAsyncEnumerable<string> HandleAsync(ExternalStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return nameof(ExternalStreamHandler);
    }
}

/// <summary>Holds a handler declared as a nested type, which discovery must still find.</summary>
public static class ExternalNesting
{
    public sealed record NestedQuery : IQuery<string>;

    public sealed class NestedQueryHandler : IQueryHandler<NestedQuery, string>
    {
        public Task<string> HandleAsync(NestedQuery query, CancellationToken cancellationToken)
            => Task.FromResult(nameof(NestedQueryHandler));
    }
}
