using Davish.Sendr;

namespace AbstractionsConsumer.Tests;

/// <summary>
/// This project references only Davish.Sendr.Abstractions — never Davish.Sendr (the
/// implementation package) — so a domain/application layer can depend on the contracts alone,
/// as the README and the Abstractions csproj description promise. The test class existing and
/// this project compiling at all is the actual assertion; the method bodies below just need
/// every referenced type to resolve.
/// </summary>
public class AbstractionsConsumerTests
{
    [Fact]
    public void GivenAbstractionsOnly_WhenDeclaringSenderAndPublisher_ThenTypesResolve()
    {
        ISender? sender = null;
        IStreamSender? streamSender = null;
        IPublisher? publisher = null;

        Assert.Null(sender);
        Assert.Null(streamSender);
        Assert.Null(publisher);
    }
}
