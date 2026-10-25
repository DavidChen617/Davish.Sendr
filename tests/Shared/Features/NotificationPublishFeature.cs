using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class NotificationPublishFeature : IFeature
{
    public string Name => "notification-publish";

    public string? Description =>
        "How IPublisher.PublishAsync runs a notification's handlers: Sequence and Parallel groups or the generated RunMode, failures that do not stop other handlers, and how exceptions reach the caller.";
}
