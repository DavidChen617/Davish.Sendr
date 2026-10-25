using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class RequestDispatchFeature : IFeature
{
    public string Name => "request-dispatch";

    public string? Description =>
        "How ISender.SendAsync finds the handler for a request, command or query and hands its result, failure and cancellation token back.";
}
