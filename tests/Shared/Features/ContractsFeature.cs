using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class ContractsFeature : IFeature
{
    public string Name => "contracts";

    public string? Description =>
        "Which assemblies the public contract types live in, and how the message and sender interfaces relate to each other.";
}
