using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class SourceGenerationFeature : IFeature
{
    public string Name => "source-generation";

    public string? Description =>
        "What Davish.Sendr.Generators does at compile time: opt-in, which handlers it discovers in this and included assemblies, and the SENDR diagnostics it reports.";
}
