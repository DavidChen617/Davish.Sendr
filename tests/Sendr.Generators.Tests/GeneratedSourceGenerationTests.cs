using Davish.JustBdd;
using Davish.Sendr;
using Generator.Tests.HandlerLibrary;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Generators.Tests;

/// <summary>
/// source-generation, observed at run time through this project's own generated sender and publisher.
/// Scenarios that need code which must not compile, or another assembly's shape, are in the harness half.
/// </summary>
[Feature<SourceGenerationFeature>]
public partial class GeneratedSourceGenerationTests
{
    // ---- Opt-in ----

    [Fact]
    public async Task GivenGeneratorReferencedButUseGeneratorsNotCalled_WhenSendingMessageWithOnlyDiscoveredHandler_ThenDefaultSenderReportsMissingRegistration()
    {
        await new Scenario()
            .Given("AddSendr() without UseGenerators(), and ProbeRequest whose handler only the generator knows about", () =>
                new ServiceCollection().AddSendr().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISender>())
            .When("ProbeRequest is sent and awaited", sender => sender.SendAsync(new ProbeRequest(1), CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The default sender reports the handler as unregistered")
                .Contains("The message asks for services.AddRequestHandler<...>()", "services.AddRequestHandler<", thrown => thrown.Exception.Message)
                .DoesNotContain("The message does not come from the generated sender", "discovered by the source generator", thrown => thrown.Exception.Message)
            .RunAsync();
    }

    [Fact]
    public async Task GivenGeneratorReferencedButUseGeneratorsNotCalled_WhenPublishingNotificationWithOnlyDiscoveredHandler_ThenHandlerDoesNotRun()
    {
        var probe = new NotificationProbe();

        await new Scenario()
            .Given("AddSendrNotification() without UseGenerators(), and BasedNote whose handler only the generator knows about", () =>
                new ServiceCollection().AddSingleton(probe).AddScoped<ScopedMarker>().AddSendrNotification()
                    .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPublisher>())
            .When("BasedNote is published and awaited", publisher => publisher.PublishAsync(new BasedNote(), CancellationToken.None))
            .Then("The publish completed without running the discovered handler")
                .Empty("BasedNoteHandler never ran", _ => probe.Log)
            .RunAsync();
    }

    // ---- Manual registrations are ignored once UseGenerators() is on ----

    [Fact]
    public async Task GivenHandlerRegisteredManuallyAlongsideUseGenerators_WhenSendAsync_ThenManualHandlerIsNotUsed()
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given("UseGenerators(), plus AddRequestHandler for ManualOnlyRequest with a handler the generator skips (open generic)", () =>
                new ServiceCollection()
                    .AddSingleton(probe)
                    .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
                    .AddRequestHandler<ManualOnlyRequest, ManualOnlyHandler<int>>()
                    .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISender>())
            .When("ManualOnlyRequest is sent and awaited", sender => sender.SendAsync(new ManualOnlyRequest(), CancellationToken.None))
            .ThenThrows<InvalidOperationException>("The generated sender reports that no handler was discovered")
                .Contains("The message points at the source generator", "discovered by the source generator", thrown => thrown.Exception.Message)
            .And("The manually registered handler never ran")
                .Empty("ManualOnlyHandler wrote nothing", _ => probe.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenNotificationHandlerRegisteredManuallyAlongsideUseGenerators_WhenPublishAsync_ThenManualHandlerIsNotUsed()
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given("UseGenerators() for notifications, plus AddNotificationHandler for ManualOnlyNote with a handler the generator skips", () =>
                new ServiceCollection()
                    .AddSingleton(probe)
                    .AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
                    .AddNotificationHandler<ManualOnlyNote>(x => x.Handler.Sequence.With<ManualOnlyNoteHandler<int>>())
                    .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPublisher>())
            .When("ManualOnlyNote is published and awaited", publisher => publisher.PublishAsync(new ManualOnlyNote(), CancellationToken.None))
            .Then("The publish completed without running the manually registered handler")
                .Empty("ManualOnlyNoteHandler wrote nothing", _ => probe.Log)
            .RunAsync();
    }

    // ---- Discovery in this compilation ----

    private static readonly IReadOnlyDictionary<string, Func<IServiceProvider, Task<object?>>> RecordHandlerCases =
        PipelineCases.For<RecRequest, RecRequestForValue, RecCommand, RecCommandForValue, RecQuery, RecStream, RecNote>();

    private static readonly IReadOnlyDictionary<string, string> RecordHandlerNames = new Dictionary<string, string>
    {
        ["SendAsync(IRequest)"] = nameof(RecRequestHandler),
        ["SendAsync(IRequest<T>)"] = nameof(RecRequestForValueHandler),
        ["SendAsync(ICommand)"] = nameof(RecCommandHandler),
        ["SendAsync(ICommand<T>)"] = nameof(RecCommandForValueHandler),
        ["SendAsync(IQuery<T>)"] = nameof(RecQueryHandler),
        [PipelineCases.SendStream] = nameof(RecStreamHandler),
        [PipelineCases.PublishAsync] = nameof(RecNoteHandler),
    };

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindData), MemberType = typeof(PipelineCases))]
    public async Task GivenRecordClassHandlers_WhenDispatched_ThenGeneratedSenderAndPublisherRunThem(string kind)
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given("A record class handler for each handler interface, none registered by hand, and UseGenerators() on both sides", () =>
                GeneratedProvider(services => services.AddSingleton(probe)))
            .When($"The {kind} record handler is dispatched to", provider => RecordHandlerCases[kind](provider.CreateScope().ServiceProvider))
            .Then("The record class handler ran")
                .Equal($"{RecordHandlerNames[kind]} ran once", [RecordHandlerNames[kind]], _ => probe.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenOneClassImplementingSeveralHandlerInterfaces_WhenEachMessageIsDispatched_ThenThatClassHandlesEach()
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given("MultiRoleHandler handles a request, a query and a notification, and UseGenerators() is on both sides", () =>
                GeneratedProvider(services => services.AddSingleton(probe)).CreateScope().ServiceProvider)
            .When("The request is sent, the query is sent and the notification is published", async provider =>
            {
                await provider.GetRequiredService<ISender>().SendAsync(new MultiRoleRequest(), CancellationToken.None);
                await provider.GetRequiredService<ISender>().SendAsync(new MultiRoleQuery(), CancellationToken.None);
                await provider.GetRequiredService<IPublisher>().PublishAsync(new MultiRoleNote(), CancellationToken.None);
            })
            .Then("MultiRoleHandler handled each one")
                .Equal("request, query, notification", ["MultiRole request", "MultiRole query", "MultiRole notification"], _ => probe.Log)
            .RunAsync();
    }

    // ---- Decorator registration ----

    [Fact]
    public async Task GivenConcreteDecoratorNotRegisteredByUser_WhenDispatchedTwiceInSameScope_ThenNewDecoratorInstanceRunsEachTime()
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given("SingleRequestHandler carries [DecorateWith<SingleDecorator>], and nobody registered SingleDecorator", () =>
                GeneratedProvider(services => services.AddSingleton(probe)).CreateScope().ServiceProvider)
            .When("SingleRequest is sent twice in the same scope", async scope =>
            {
                await PipelineCases.Single["SendAsync(IRequest)"](scope);
                await PipelineCases.Single["SendAsync(IRequest)"](scope);
            })
            .Then("The decorator ran on both sends")
                .Equal("Two decorator runs", 2, _ => probe.DecoratorInterfaces.Count)
            .And("Each send got a new decorator instance")
                .Distinct("Two different decorator objects", _ => probe.DecoratorsConstructed, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }

    [Fact]
    public async Task GivenDecoratorPreRegisteredByUserBeforeUseGenerators_WhenDispatched_ThenUserRegistrationIsUsed()
    {
        var probe = new PipelineProbe();
        var userDecorator = new SingleDecorator(probe);

        await new Scenario()
            .Given("A SingleDecorator instance registered as a singleton before UseGenerators()", () =>
                GeneratedProvider(services => services.AddSingleton(probe).AddSingleton(userDecorator)).CreateScope().ServiceProvider)
            .When("SingleRequest is sent", scope => PipelineCases.Single["SendAsync(IRequest)"](scope))
            .Then("The user's instance is the only decorator, and it ran", _ =>
            {
                Assert.Same(userDecorator, Assert.Single(probe.DecoratorsConstructed));
                Assert.Equal([nameof(Davish.Sendr.IRequestDecorator)], probe.DecoratorInterfaces);
            })
            .RunAsync();
    }

    public static TheoryData<string> DecoratorAbstractions => new() { "interface", "abstract class" };

    [Theory]
    [MemberData(nameof(DecoratorAbstractions))]
    public async Task GivenInterfaceOrAbstractDecoratorRegisteredAfterUseGenerators_WhenDispatched_ThenProviderBuildsAndRegisteredImplementationRuns(string abstraction)
    {
        var probe = new PipelineProbe();

        await new Scenario()
            .Given($"A handler decorated with an {abstraction}, whose implementation the application registers only after UseGenerators()", () => (Func<ServiceProvider>)(() =>
            {
                var services = new ServiceCollection()
                    .AddSingleton(probe)
                    .AddDispatchProbe(new DispatchProbe())
                    .AddSingleton<ExternalLogCollector>()
                    .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()));
                if (abstraction == "interface")
                    services.AddTransient<IAppDecorator, AppDecorator>();
                else
                    services.AddTransient<AppDecoratorBase, AppDecoratorImpl>();
                return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
            }))
            .When("The provider is built with ValidateOnBuild and the handler is dispatched to", async build =>
            {
                var sender = build().CreateScope().ServiceProvider.GetRequiredService<ISender>();
                if (abstraction == "interface")
                    await sender.SendAsync(new AppInterfaceDecoratedRequest(), CancellationToken.None);
                else
                    await sender.SendAsync(new AppAbstractDecoratedRequest(), CancellationToken.None);
            })
            .Then("The provider built and the application's implementation wrapped the handler")
                .Equal("The registered decorator ran before the handler",
                    abstraction == "interface"
                        ? [nameof(AppDecorator), nameof(AppInterfaceDecoratedRequestHandler)]
                        : [nameof(AppDecoratorImpl), nameof(AppAbstractDecoratedRequestHandler)],
                    _ => probe.Log)
            .RunAsync();
    }

    // ---- Cross-assembly inclusion, at run time ----

    private static readonly IReadOnlyDictionary<string, (Func<IServiceProvider, Task<object?>> Dispatch, object Expected)> ExternalCases =
        new Dictionary<string, (Func<IServiceProvider, Task<object?>>, object)>
        {
            ["SendAsync(IRequest)"] = (async sp =>
            {
                await sp.GetRequiredService<ISender>().SendAsync(new ExternalRequest(), CancellationToken.None);
                return string.Join(",", sp.GetRequiredService<ExternalLogCollector>().Log);
            }, nameof(ExternalRequestHandler)),
            ["SendAsync(IRequest<T>)"] = (async sp => await sp.GetRequiredService<ISender>().SendAsync(new ExternalRequestForValue(), CancellationToken.None),
                nameof(ExternalRequestForValueHandler)),
            ["SendAsync(ICommand)"] = (async sp =>
            {
                await sp.GetRequiredService<ISender>().SendAsync(new ExternalCommand(), CancellationToken.None);
                return string.Join(",", sp.GetRequiredService<ExternalLogCollector>().Log);
            }, "ExternalCommandHandled"),
            ["SendAsync(ICommand<T>)"] = (async sp => await sp.GetRequiredService<ISender>().SendAsync(new ExternalCommandForValue(), CancellationToken.None),
                nameof(ExternalCommandForValueHandler)),
            ["SendAsync(IQuery<T>)"] = (async sp => (await sp.GetRequiredService<ISender>().SendAsync(new ExternalQuery(), CancellationToken.None)).Value,
                "from-library"),
            [PipelineCases.SendStream] = (async sp =>
            {
                var items = new List<string>();
                await foreach (var item in sp.GetRequiredService<IStreamSender>().SendStream(new ExternalStream(), CancellationToken.None))
                    items.Add(item);
                return string.Join(",", items);
            }, nameof(ExternalStreamHandler)),
        };

    [Theory]
    [MemberData(nameof(PipelineCases.AllKindsButPublishData), MemberType = typeof(PipelineCases))]
    public async Task GivenSenderIncludesAssemblyOfMarker_WhenMessageHandledInThatAssemblyIsDispatched_ThenExternalHandlerRuns(string kind)
    {
        var (dispatch, expected) = ExternalCases[kind];

        await new Scenario()
            .Given("AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>())), where that library has no generator of its own", () =>
                new ServiceCollection()
                    .AddSingleton<ExternalLogCollector>()
                    .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
                    .BuildServiceProvider().CreateScope().ServiceProvider)
            .When($"The library's {kind} handler is dispatched to", scope => dispatch(scope))
            .Then("The external handler ran and its answer reached the caller")
                .Equal("The external handler's answer or log entry", expected, result => result)
            .RunAsync();
    }

    [Fact]
    public async Task GivenPublisherIncludesAssemblyOfMarker_WhenNotificationHandledInThatAssemblyIsPublished_ThenExternalHandlerRuns()
    {
        await new Scenario()
            .Given("AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))", () =>
                new ServiceCollection()
                    .AddSingleton<ExternalLogCollector>()
                    .AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
                    .BuildServiceProvider().CreateScope().ServiceProvider)
            .When("The library's ExternalNotification is published", async scope =>
            {
                await scope.GetRequiredService<IPublisher>().PublishAsync(new ExternalNotification(), CancellationToken.None);
                return scope.GetRequiredService<ExternalLogCollector>();
            })
            .Then("The external notification handler ran")
                .Equal("It logged once", ["ExternalNotificationHandled"], collector => collector.Log)
            .RunAsync();
    }

    [Fact]
    public async Task GivenNestedHandlerTypeInIncludedAssembly_WhenDispatched_ThenItRuns()
    {
        await new Scenario()
            .Given("The included library declares NestedQueryHandler as a public nested type", () =>
                new ServiceCollection()
                    .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
                    .BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ISender>())
            .When("NestedQuery is sent", sender => sender.SendAsync(new ExternalNesting.NestedQuery(), CancellationToken.None))
            .Then("The nested handler answered")
                .Equal("The answer names NestedQueryHandler", nameof(ExternalNesting.NestedQueryHandler), answer => answer)
            .RunAsync();
    }

    // ---- Chaining on the generated options ----

    public static TheoryData<string> ChainOrders => new() { "IncludeAssemblyOf then RunAs", "RunAs then IncludeAssemblyOf" };

    [Theory]
    [MemberData(nameof(ChainOrders))]
    public void GivenRunAsChainedWithIncludeAssemblyOf_WhenConfigured_ThenOptionsRunModeIsTheOneSet(string order)
    {
        new Scenario()
            .Given("A new GeneratedNotificationRunOptions", () => new GeneratedNotificationRunOptions())
            .When($"It is configured with {order}", options =>
            {
                if (order == "IncludeAssemblyOf then RunAs")
                    options.IncludeAssemblyOf<HandlerLibraryMarker>().RunAs(NotificationRunMode.Parallel);
                else
                    options.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<HandlerLibraryMarker>();
            })
            .Then("The original options object holds the mode")
                .Equal("RunMode is Parallel", NotificationRunMode.Parallel, options => options.RunMode)
            .Run();
    }

    private static ServiceProvider GeneratedProvider(Func<IServiceCollection, IServiceCollection> add) =>
        add(new ServiceCollection())
            .AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
            .AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<HandlerLibraryMarker>()))
            .BuildServiceProvider();
}

// ---- Fixtures only this project needs ----

public sealed record RecRequest : IRequest;
public sealed record RecRequestForValue : IRequest<int>;
public sealed record RecCommand : ICommand;
public sealed record RecCommandForValue : ICommand<int>;
public sealed record RecQuery : IQuery<int>;
public sealed record RecStream : IStreamRequest<int>;
public sealed record RecNote : INotification;

public sealed record RecRequestHandler(PipelineProbe Probe) : IRequestHandler<RecRequest>
{
    public Task HandleAsync(RecRequest request, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecRequestHandler));
        return Task.CompletedTask;
    }
}

public sealed record RecRequestForValueHandler(PipelineProbe Probe) : IRequestHandler<RecRequestForValue, int>
{
    public Task<int> HandleAsync(RecRequestForValue request, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecRequestForValueHandler));
        return Task.FromResult(1);
    }
}

public sealed record RecCommandHandler(PipelineProbe Probe) : ICommandHandler<RecCommand>
{
    public Task HandleAsync(RecCommand command, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecCommandHandler));
        return Task.CompletedTask;
    }
}

public sealed record RecCommandForValueHandler(PipelineProbe Probe) : ICommandHandler<RecCommandForValue, int>
{
    public Task<int> HandleAsync(RecCommandForValue command, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecCommandForValueHandler));
        return Task.FromResult(1);
    }
}

public sealed record RecQueryHandler(PipelineProbe Probe) : IQueryHandler<RecQuery, int>
{
    public Task<int> HandleAsync(RecQuery query, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecQueryHandler));
        return Task.FromResult(1);
    }
}

public sealed record RecStreamHandler(PipelineProbe Probe) : IStreamRequestHandler<RecStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(RecStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecStreamHandler));
        await Task.Yield();
        yield return 1;
    }
}

public sealed record RecNoteHandler(PipelineProbe Probe) : INotificationHandler<RecNote>
{
    public Task HandleAsync(RecNote notification, CancellationToken cancellationToken)
    {
        Probe.Write(nameof(RecNoteHandler));
        return Task.CompletedTask;
    }
}

public sealed record MultiRoleRequest : IRequest;
public sealed record MultiRoleQuery : IQuery<int>;
public sealed record MultiRoleNote : INotification;

public sealed class MultiRoleHandler(PipelineProbe probe) :
    IRequestHandler<MultiRoleRequest>,
    IQueryHandler<MultiRoleQuery, int>,
    INotificationHandler<MultiRoleNote>
{
    public Task HandleAsync(MultiRoleRequest request, CancellationToken cancellationToken)
    {
        probe.Write("MultiRole request");
        return Task.CompletedTask;
    }

    public Task<int> HandleAsync(MultiRoleQuery query, CancellationToken cancellationToken)
    {
        probe.Write("MultiRole query");
        return Task.FromResult(1);
    }

    public Task HandleAsync(MultiRoleNote notification, CancellationToken cancellationToken)
    {
        probe.Write("MultiRole notification");
        return Task.CompletedTask;
    }
}

// Open generic, so the generator skips it (SENDR003, Info) while AddRequestHandler can still register a closed form.
public sealed record ManualOnlyRequest : IRequest;

public sealed class ManualOnlyHandler<TTag>(PipelineProbe probe) : IRequestHandler<ManualOnlyRequest>
{
    public Task HandleAsync(ManualOnlyRequest request, CancellationToken cancellationToken)
    {
        probe.Write(nameof(ManualOnlyHandler<TTag>));
        return Task.CompletedTask;
    }
}

public sealed record ManualOnlyNote : INotification;

public sealed class ManualOnlyNoteHandler<TTag>(PipelineProbe probe) : INotificationHandler<ManualOnlyNote>
{
    public Task HandleAsync(ManualOnlyNote notification, CancellationToken cancellationToken)
    {
        probe.Write(nameof(ManualOnlyNoteHandler<TTag>));
        return Task.CompletedTask;
    }
}

// Decorators the application, not the generator, has to register.
public interface IAppDecorator : IRequestDecorator;

public sealed class AppDecorator(PipelineProbe probe) : IAppDecorator
{
    public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        probe.Write(nameof(AppDecorator));
        return next();
    }
}

public abstract class AppDecoratorBase : IRequestDecorator
{
    public abstract Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest;
}

public sealed class AppDecoratorImpl(PipelineProbe probe) : AppDecoratorBase
{
    public override Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        probe.Write(nameof(AppDecoratorImpl));
        return next();
    }
}

public sealed record AppInterfaceDecoratedRequest : IRequest;

[DecorateWith<IAppDecorator>]
public sealed class AppInterfaceDecoratedRequestHandler(PipelineProbe probe) : IRequestHandler<AppInterfaceDecoratedRequest>
{
    public Task HandleAsync(AppInterfaceDecoratedRequest request, CancellationToken cancellationToken)
    {
        probe.Write(nameof(AppInterfaceDecoratedRequestHandler));
        return Task.CompletedTask;
    }
}

public sealed record AppAbstractDecoratedRequest : IRequest;

[DecorateWith<AppDecoratorBase>]
public sealed class AppAbstractDecoratedRequestHandler(PipelineProbe probe) : IRequestHandler<AppAbstractDecoratedRequest>
{
    public Task HandleAsync(AppAbstractDecoratedRequest request, CancellationToken cancellationToken)
    {
        probe.Write(nameof(AppAbstractDecoratedRequestHandler));
        return Task.CompletedTask;
    }
}
