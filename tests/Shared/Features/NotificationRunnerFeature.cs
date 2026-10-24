using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class NotificationRunnerFeature : IFeature
{
    public string Name => "notification-runner";

    public string? Description =>
        "NotificationGroupRunner, the public runner both publishers share: step snapshots, execution order and concurrency, and exception collection.";
}
