using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class HandlerLifetimeFeature : IFeature
{
    public string Name => "handler-lifetime";

    public string? Description =>
        "When handlers and decorators are constructed and disposed, including asynchronous disposal and a decorator that fails to construct.";
}
