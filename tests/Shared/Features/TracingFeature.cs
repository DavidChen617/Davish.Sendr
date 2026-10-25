using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class TracingFeature : IFeature
{
    public string Name => "tracing";

    public string? Description =>
        "The spans Sendr emits through the Davish.Sendr ActivitySource for send and publish, and that listening to them never changes dispatch results.";
}
