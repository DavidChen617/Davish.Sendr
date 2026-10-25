using Davish.JustBdd;

namespace Sendr.TestFeatures;

public sealed class ServiceRegistrationFeature : IFeature
{
    public string Name => "service-registration";

    public string? Description =>
        "How AddSendr, AddSendrNotification and the AddXxxHandler family register senders, publishers, handlers and decorators.";
}
