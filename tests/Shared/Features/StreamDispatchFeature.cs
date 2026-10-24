using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class StreamDispatchFeature : IFeature
{
    public string Name => "stream-dispatch";

    public string? Description =>
        "How IStreamSender.SendStream defers to the stream handler until enumeration, yields its items and surfaces failures and cancellation.";
}
