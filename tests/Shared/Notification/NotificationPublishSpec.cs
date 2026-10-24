using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

namespace Sendr.TestFixtures;

/// <summary>
/// notification-publish scenarios whose setup is the same for both publishers. Scenarios that need the default
/// publisher's Sequence / Parallel groups or the generated publisher's RunMode live in the derived classes.
/// </summary>
public abstract class NotificationPublishSpec
{
    /// <summary>
    /// Registers the publisher under test so that BasedNote and ProbeNote have handlers and OrphanNote has none.
    /// </summary>
    protected abstract IServiceCollection AddPublisherUnderTest(IServiceCollection services);

    [Fact]
    public async Task GivenNoHandlersForNotificationType_WhenPublishAsync_ThenCompletesWithoutException()
    {
        await new Scenario()
            .Given<NoteContext>("A publisher with handlers for other notification types but none for OrphanNote", ctx => ctx.Build(AddPublisherUnderTest))
            .When("OrphanNote is published and awaited", ctx => ctx.Publisher.PublishAsync(new OrphanNote(), CancellationToken.None))
            .Then("The publish completed and no handler ran")
                .Empty("No handler wrote anything", ctx => ctx.Probe.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenNullNotification_WhenPublishAsync_ThenThrowsArgumentNullExceptionWithParamName()
    {
        await new Scenario()
            .Given<NoteContext>("A publisher", ctx => ctx.Build(AddPublisherUnderTest))
            .When("PublishAsync is called with null and awaited", ctx => ctx.Publisher.PublishAsync(null!, CancellationToken.None))
            .ThenThrows<ArgumentNullException>("The null notification is rejected")
                .Equal("The parameter is named 'notification'", "notification", thrown => thrown.Exception.ParamName)
            .RunAsync();
    }

    public static TheoryData<string> StaticTypes => new() { "INotification", "base type" };

    [Theory]
    [MemberData(nameof(StaticTypes))]
    public async Task GivenNotificationReferencedAsBaseType_WhenPublishAsync_ThenHandlersForRuntimeTypeRun(string staticType)
    {
        await new Scenario()
            .Given<NoteContext>("A handler for the concrete BasedNote type", ctx => ctx.Build(AddPublisherUnderTest))
            .When($"A BasedNote held in a variable typed as {staticType} is published", ctx =>
            {
                INotification asInterface = new BasedNote();
                NoteBase asBase = new BasedNote();
                return ctx.Publisher.PublishAsync(staticType == "INotification" ? asInterface : asBase, CancellationToken.None);
            })
            .Then("The runtime type's handler ran")
                .Equal("BasedNoteHandler ran to the end", ["BasedNoteHandler start", "BasedNoteHandler end"], ctx => ctx.Probe.Log)
            .RunAsync();
    }

    /// <summary>The state every notification-publish scenario starts from.</summary>
    public sealed class NoteContext
    {
        public NotificationProbe Probe { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public IServiceScope Scope { get; private set; } = null!;
        public IPublisher Publisher { get; private set; } = null!;

        public void Build(Func<IServiceCollection, IServiceCollection> addPublisher)
        {
            Provider = addPublisher(new ServiceCollection().AddSingleton(Probe).AddScoped<ScopedMarker>()).BuildServiceProvider();
            Scope = Provider.CreateScope();
            Publisher = Scope.ServiceProvider.GetRequiredService<IPublisher>();
        }
    }
}
