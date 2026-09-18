using Davish.Sendr;

namespace Generator.Tests.HandlerLibrary;

/// <summary>
/// Marker type passed to <c>IncludeAssemblyOf&lt;HandlerLibraryMarker&gt;()</c> by the host test
/// project so its generator run discovers the handlers below, which live in this separate
/// assembly and are otherwise invisible to a generator that only ever sees its own compilation's
/// syntax trees.
/// </summary>
public sealed class HandlerLibraryMarker;

public sealed class ExternalLogCollector
{
    public List<string> Log { get; } = [];
}

public sealed record ExternalQuery : IQuery<ExternalDto>;

public sealed record ExternalDto(string Value);

public sealed class ExternalQueryHandler : IQueryHandler<ExternalQuery, ExternalDto>
{
    public Task<ExternalDto> HandleAsync(ExternalQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new ExternalDto("from-library"));
}

public sealed record ExternalCommand : ICommand;

public sealed class ExternalCommandHandler(ExternalLogCollector collector) : ICommandHandler<ExternalCommand>
{
    public Task HandleAsync(ExternalCommand command, CancellationToken cancellationToken)
    {
        collector.Log.Add("ExternalCommandHandled");
        return Task.CompletedTask;
    }
}

public sealed record ExternalNotification : INotification;

public sealed class ExternalNotificationHandler(ExternalLogCollector collector) : INotificationHandler<ExternalNotification>
{
    public Task HandleAsync(ExternalNotification notification, CancellationToken cancellationToken)
    {
        collector.Log.Add("ExternalNotificationHandled");
        return Task.CompletedTask;
    }
}
