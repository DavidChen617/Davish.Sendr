using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class DecoratorsFeature : IFeature
{
    public string Name => "decorators";

    public string? Description =>
        "How decorators wrap a handler: what runs before and after it, in which order several decorators nest, and what happens when one does not call next.";
}
