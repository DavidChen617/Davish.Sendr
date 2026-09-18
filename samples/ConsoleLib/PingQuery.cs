using Davish.Sendr;

namespace ConsoleLib;

public sealed record PingQuery : IQuery<string>;

public sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<string> HandleAsync(PingQuery query, CancellationToken cancellationToken)
        => Task.FromResult("Pong from ConsoleLib!");
}
