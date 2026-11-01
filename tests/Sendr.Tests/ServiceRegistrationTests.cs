using System.Runtime.CompilerServices;
using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;

namespace Sendr.Tests;

[Feature<ServiceRegistrationFeature>]
public class ServiceRegistrationTests
{
    // ---- AddSendr default registration ----

    [Fact]
    public async Task GivenAddSendrWithoutConfigure_WhenResolvingISender_ThenItDispatchesToManuallyRegisteredHandlers()
    {
        await new Scenario()
            .Given<SenderContext>("AddSendr without configure and a request handler registered with AddRequestHandler", ctx =>
                ctx.Provider = new ServiceCollection()
                    .AddSingleton<CallLog>()
                    .AddSendr()
                    .AddRequestHandler<Ping, PingHandler>()
                    .BuildServiceProvider())
            .When("ISender is resolved from a scope and the request is sent", async ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                ctx.Sender = scope.ServiceProvider.GetRequiredService<ISender>();
                await ctx.Sender.SendAsync(new Ping(), CancellationToken.None);
            })
            .Then("A sender is resolved")
                .NotNull("The resolved ISender is not null", ctx => ctx.Sender)
            .And("The registered handler handled the request")
                .Equal("The handler ran once", ["PingHandler"], ctx => ctx.Provider.GetRequiredService<CallLog>().Calls)
            .RunAsync();
    }

    [Fact]
    public void GivenDefaultSender_WhenResolvingISenderAndIStreamSenderInSameScope_ThenBothAreSameInstance()
    {
        new Scenario()
            .Given<SenderContext>("AddSendr without configure", ctx =>
                ctx.Provider = new ServiceCollection().AddSendr().BuildServiceProvider())
            .When("ISender and IStreamSender are resolved from the same scope", ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                ctx.Sender = scope.ServiceProvider.GetRequiredService<ISender>();
                ctx.StreamSender = scope.ServiceProvider.GetRequiredService<IStreamSender>();
            })
            .Then("Both resolve to one instance")
                .True("ISender and IStreamSender are the same object", ctx => ReferenceEquals(ctx.Sender, ctx.StreamSender))
            .Run();
    }

    [Fact]
    public void GivenAddSendr_WhenResolvingISenderAcrossScopes_ThenInstanceIsScoped()
    {
        new Scenario()
            .Given<ScopedResolution<ISender>>("AddSendr", ctx =>
                ctx.Provider = new ServiceCollection().AddSendr().BuildServiceProvider())
            .When("ISender is resolved twice in one scope and once in another", ctx => ctx.ResolveAcrossScopes())
            .Then("The instance is shared within a scope")
                .True("Both resolutions in the first scope are the same object", ctx => ReferenceEquals(ctx.First, ctx.SecondInSameScope))
            .And("The instance is not shared across scopes")
                .False("The other scope gets a different object", ctx => ReferenceEquals(ctx.First, ctx.InOtherScope))
            .Run();
    }

    // ---- UseSender ----

    [Fact]
    public void GivenUseSender_WhenResolvingISender_ThenCustomSenderIsReturnedWithInjectedDependencies()
    {
        new Scenario()
            .Given<SenderContext>("A dependency registered before AddSendr(o => o.UseSender<CustomSender>())", ctx =>
                ctx.Provider = new ServiceCollection()
                    .AddSingleton<SenderDependency>()
                    .AddSendr(o => o.UseSender<CustomSender>())
                    .BuildServiceProvider())
            .When("ISender is resolved from a scope", ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                ctx.Sender = scope.ServiceProvider.GetRequiredService<ISender>();
            })
            .Then("The custom sender is returned")
                .IsType("The sender is CustomSender", typeof(CustomSender), ctx => ctx.Sender)
            .And("It holds the dependency from the container")
                .True("The injected dependency is the registered instance", ctx =>
                    ReferenceEquals(((CustomSender)ctx.Sender!).Dependency, ctx.Provider.GetRequiredService<SenderDependency>()))
            .Run();
    }

    [Fact]
    public void GivenUseSender_WhenResolvingAllISenderRegistrations_ThenOnlyCustomSenderIsRegistered()
    {
        new Scenario()
            .Given("AddSendr(o => o.UseSender<CustomSender>())", () =>
                new ServiceCollection()
                    .AddSingleton<SenderDependency>()
                    .AddSendr(o => o.UseSender<CustomSender>())
                    .BuildServiceProvider())
            .When("Every ISender registration is resolved from a scope", provider =>
                provider.CreateScope().ServiceProvider.GetServices<ISender>().ToArray())
            .Then("Only the custom sender is registered")
                .Single("There is exactly one ISender and it is a CustomSender", senders => senders, sender => sender is CustomSender)
            .Run();
    }

    [Fact]
    public void GivenDisposableCustomSender_WhenScopeResolvesOnlyISender_ThenConstructedOnceAndDisposedOnce()
    {
        new Scenario()
            .Given("A disposable custom sender installed with UseSender", () =>
                new ServiceCollection()
                    .AddSingleton<DisposeProbe>()
                    .AddSendr(o => o.UseSender<DisposableCustomSender>())
                    .BuildServiceProvider())
            .When("A scope resolves only ISender and then ends", provider =>
            {
                using (var scope = provider.CreateScope())
                    scope.ServiceProvider.GetRequiredService<ISender>();
                return provider.GetRequiredService<DisposeProbe>();
            })
            .Then("The sender was constructed once")
                .Equal("Constructed once", 1, probe => probe.Constructed)
            .And("The sender was disposed once")
                .Equal("Disposed once", 1, probe => probe.Disposed)
            .Run();
    }

    [Fact]
    public void GivenDisposableCustomSender_WhenScopeResolvesISenderAndIStreamSender_ThenConstructedOnceAndDisposedTwice()
    {
        new Scenario()
            .Given<SenderContext>("A disposable custom sender installed with UseSender", ctx =>
                ctx.Provider = new ServiceCollection()
                    .AddSingleton<DisposeProbe>()
                    .AddSendr(o => o.UseSender<DisposableCustomSender>())
                    .BuildServiceProvider())
            .When("A scope resolves both ISender and IStreamSender and then ends", ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                ctx.Sender = scope.ServiceProvider.GetRequiredService<ISender>();
                ctx.StreamSender = scope.ServiceProvider.GetRequiredService<IStreamSender>();
            })
            .Then("Both resolve to one instance")
                .True("ISender and IStreamSender are the same object", ctx => ReferenceEquals(ctx.Sender, ctx.StreamSender))
            .And("The sender was constructed once")
                .Equal("Constructed once", 1, ctx => ctx.Provider.GetRequiredService<DisposeProbe>().Constructed)
            .And("The container disposed it once per resolved registration")
                .Equal("Disposed twice", 2, ctx => ctx.Provider.GetRequiredService<DisposeProbe>().Disposed)
            .Run();
    }

    // ---- AddSendrNotification ----

    [Fact]
    public void GivenAddSendrNotification_WhenResolvingIPublisher_ThenPublisherIsReturned()
    {
        new Scenario()
            .Given("AddSendrNotification without configure", () =>
                new ServiceCollection().AddSendrNotification().BuildServiceProvider())
            .When("IPublisher is resolved from a scope", provider =>
                provider.CreateScope().ServiceProvider.GetRequiredService<IPublisher>())
            .Then("A publisher is returned")
                .NotNull("The resolved IPublisher is not null", publisher => publisher)
            .Run();
    }

    [Fact]
    public void GivenAddSendrNotification_WhenResolvingIPublisherAcrossScopes_ThenInstanceIsScoped()
    {
        new Scenario()
            .Given<ScopedResolution<IPublisher>>("AddSendrNotification", ctx =>
                ctx.Provider = new ServiceCollection().AddSendrNotification().BuildServiceProvider())
            .When("IPublisher is resolved twice in one scope and once in another", ctx => ctx.ResolveAcrossScopes())
            .Then("The instance is shared within a scope")
                .True("Both resolutions in the first scope are the same object", ctx => ReferenceEquals(ctx.First, ctx.SecondInSameScope))
            .And("The instance is not shared across scopes")
                .False("The other scope gets a different object", ctx => ReferenceEquals(ctx.First, ctx.InOtherScope))
            .Run();
    }

    [Fact]
    public void GivenUsePublisher_WhenResolvingIPublisher_ThenCustomPublisherIsReturnedWithInjectedDependencies()
    {
        new Scenario()
            .Given<PublisherContext>("A dependency registered before AddSendrNotification(o => o.UsePublisher<CustomPublisher>())", ctx =>
                ctx.Provider = new ServiceCollection()
                    .AddSingleton<SenderDependency>()
                    .AddSendrNotification(o => o.UsePublisher<CustomPublisher>())
                    .BuildServiceProvider())
            .When("IPublisher is resolved from a scope", ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                ctx.Publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            })
            .Then("The custom publisher is returned")
                .IsType("The publisher is CustomPublisher", typeof(CustomPublisher), ctx => ctx.Publisher)
            .And("It holds the dependency from the container")
                .True("The injected dependency is the registered instance", ctx =>
                    ReferenceEquals(((CustomPublisher)ctx.Publisher!).Dependency, ctx.Provider.GetRequiredService<SenderDependency>()))
            .Run();
    }

    [Fact]
    public void GivenUsePublisher_WhenResolvingAllIPublisherRegistrations_ThenOnlyCustomPublisherIsRegistered()
    {
        new Scenario()
            .Given("AddSendrNotification(o => o.UsePublisher<CustomPublisher>())", () =>
                new ServiceCollection()
                    .AddSingleton<SenderDependency>()
                    .AddSendrNotification(o => o.UsePublisher<CustomPublisher>())
                    .BuildServiceProvider())
            .When("Every IPublisher registration is resolved from a scope", provider =>
                provider.CreateScope().ServiceProvider.GetServices<IPublisher>().ToArray())
            .Then("Only the custom publisher is registered")
                .Single("There is exactly one IPublisher and it is a CustomPublisher", publishers => publishers, publisher => publisher is CustomPublisher)
            .Run();
    }

    [Fact]
    public void GivenDisposableCustomPublisher_WhenScopeEnds_ThenConstructedOnceAndDisposedOnce()
    {
        new Scenario()
            .Given("A disposable custom publisher installed with UsePublisher", () =>
                new ServiceCollection()
                    .AddSingleton<DisposeProbe>()
                    .AddSendrNotification(o => o.UsePublisher<DisposableCustomPublisher>())
                    .BuildServiceProvider())
            .When("A scope resolves IPublisher and then ends", provider =>
            {
                using (var scope = provider.CreateScope())
                    scope.ServiceProvider.GetRequiredService<IPublisher>();
                return provider.GetRequiredService<DisposeProbe>();
            })
            .Then("The publisher was constructed once")
                .Equal("Constructed once", 1, probe => probe.Constructed)
            .And("The publisher was disposed once")
                .Equal("Disposed once", 1, probe => probe.Disposed)
            .Run();
    }

    // ---- Duplicate registration guards ----

    public static TheoryData<string, Action<IServiceCollection>> SingleRegistrations => new()
    {
        { "AddRequestHandler<TRequest, THandler>", s => s.AddRequestHandler<Ping, PingHandler>() },
        { "AddRequestHandler<TRequest, TResponse, THandler>", s => s.AddRequestHandler<PingForValue, int, PingForValueHandler>() },
        { "AddStreamRequestHandler", s => s.AddStreamRequestHandler<CountUp, int, CountUpHandler>() },
        { "AddCommandHandler<TCommand, THandler>", s => s.AddCommandHandler<DoThing, DoThingHandler>() },
        { "AddCommandHandler<TCommand, TResponse, THandler>", s => s.AddCommandHandler<DoThingForValue, int, DoThingForValueHandler>() },
        { "AddQueryHandler", s => s.AddQueryHandler<GetValue, int, GetValueHandler>() },
    };

    [Theory]
    [MemberData(nameof(SingleRegistrations))]
    public void GivenHandlerAlreadyRegistered_WhenRegisteringSameMessageTypeAgain_ThenThrowsInvalidOperationException(
        string overload, Action<IServiceCollection> register)
    {
        new Scenario()
            .Given($"{overload} has already registered a handler", () =>
            {
                var services = new ServiceCollection();
                register(services);
                return (IServiceCollection)services;
            })
            .When($"{overload} is called again for the same message type", register)
            .ThenThrows<InvalidOperationException>("The duplicate registration is rejected")
            .Run();
    }

    public static TheoryData<string, Action<IServiceCollection>> ReentrantRegistrations => new()
    {
        { "AddRequestHandler<TRequest, THandler>", s => s.AddRequestHandler<Ping, PingHandler>(_ => s.AddRequestHandler<Ping, PingHandler>()) },
        { "AddRequestHandler<TRequest, TResponse, THandler>", s => s.AddRequestHandler<PingForValue, int, PingForValueHandler>(_ => s.AddRequestHandler<PingForValue, int, PingForValueHandler>()) },
        { "AddStreamRequestHandler", s => s.AddStreamRequestHandler<CountUp, int, CountUpHandler>(_ => s.AddStreamRequestHandler<CountUp, int, CountUpHandler>()) },
        { "AddCommandHandler<TCommand, THandler>", s => s.AddCommandHandler<DoThing, DoThingHandler>(_ => s.AddCommandHandler<DoThing, DoThingHandler>()) },
        { "AddCommandHandler<TCommand, TResponse, THandler>", s => s.AddCommandHandler<DoThingForValue, int, DoThingForValueHandler>(_ => s.AddCommandHandler<DoThingForValue, int, DoThingForValueHandler>()) },
        { "AddQueryHandler", s => s.AddQueryHandler<GetValue, int, GetValueHandler>(_ => s.AddQueryHandler<GetValue, int, GetValueHandler>()) },
        {
            "AddNotificationHandler",
            s => s.AddNotificationHandler<Pinged>(x =>
            {
                x.Handler.Sequence.With<PingedHandler>();
                s.AddNotificationHandler<Pinged>(y => y.Handler.Sequence.With<PingedHandler>());
            })
        },
    };

    [Theory]
    [MemberData(nameof(ReentrantRegistrations))]
    public void GivenReentrantRegistrationInsideConfigure_WhenRegisteringSameMessageType_ThenThrowsInvalidOperationException(
        string method, Action<IServiceCollection> registerReentrantly)
    {
        new Scenario()
            .Given("An empty service collection", () => (IServiceCollection)new ServiceCollection())
            .When($"{method} registers the same message type again from inside its own configure callback", registerReentrantly)
            .ThenThrows<InvalidOperationException>("The nested registration is rejected instead of silently shadowing the other")
            .Run();
    }

    [Fact]
    public void GivenNotificationHandlersAlreadyRegistered_WhenAddNotificationHandlerAgain_ThenThrowsInvalidOperationException()
    {
        new Scenario()
            .Given("AddNotificationHandler<Pinged> has already been called", () =>
                new ServiceCollection().AddNotificationHandler<Pinged>(x => x.Handler.Sequence.With<PingedHandler>()))
            .When("AddNotificationHandler<Pinged> is called again", services =>
                services.AddNotificationHandler<Pinged>(x => x.Handler.Parallel.With<PingedHandler>()))
            .ThenThrows<InvalidOperationException>("The second call is rejected")
            .Run();
    }

    public static TheoryData<string, Action<IServiceCollection>> RegistrationsPerResponseType => new()
    {
        {
            "AddRequestHandler<TRequest, TResponse, THandler>",
            s => s.AddRequestHandler<MultiRequest, int, MultiRequestIntHandler>()
                  .AddRequestHandler<MultiRequest, string, MultiRequestStringHandler>()
        },
        {
            "AddCommandHandler<TCommand, TResponse, THandler>",
            s => s.AddCommandHandler<MultiCommand, int, MultiCommandIntHandler>()
                  .AddCommandHandler<MultiCommand, string, MultiCommandStringHandler>()
        },
        {
            "AddQueryHandler",
            s => s.AddQueryHandler<MultiQuery, int, MultiQueryIntHandler>()
                  .AddQueryHandler<MultiQuery, string, MultiQueryStringHandler>()
        },
        {
            "AddStreamRequestHandler",
            s => s.AddStreamRequestHandler<MultiStream, int, MultiStreamIntHandler>()
                  .AddStreamRequestHandler<MultiStream, string, MultiStreamStringHandler>()
        },
    };

    [Theory]
    [MemberData(nameof(RegistrationsPerResponseType))]
    public void GivenMessageTypeWithTwoResponseTypes_WhenRegisteringHandlerPerResponseType_ThenNoExceptionIsThrown(
        string overload, Action<IServiceCollection> registerBoth)
    {
        new Scenario()
            .Given("A message type that implements two response types", () => (IServiceCollection)new ServiceCollection())
            .When($"{overload} registers one handler per response type", registerBoth)
            .Then("Both registrations are accepted")
                .Equal("Two handler registrations exist", 2, services => services.Count)
            .Run();
    }

    // ---- Automatic registration ----

    public static TheoryData<string, Action<IServiceCollection>, Func<IServiceProvider, Task>> DecoratedPipelines => new()
    {
        {
            "AddRequestHandler<TRequest, THandler>",
            s => s.AddRequestHandler<Ping, PingHandler>(x => x.Decorator.With<RecordingDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new Ping(), CancellationToken.None)
        },
        {
            "AddRequestHandler<TRequest, TResponse, THandler>",
            s => s.AddRequestHandler<PingForValue, int, PingForValueHandler>(x => x.Decorator.With<RecordingDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new PingForValue(), CancellationToken.None)
        },
        {
            "AddStreamRequestHandler",
            s => s.AddStreamRequestHandler<CountUp, int, CountUpHandler>(x => x.Decorator.With<RecordingDecorator>()),
            async sp => { await foreach (var _ in sp.GetRequiredService<IStreamSender>().SendStream(new CountUp(), CancellationToken.None)) { } }
        },
        {
            "AddCommandHandler<TCommand, THandler>",
            s => s.AddCommandHandler<DoThing, DoThingHandler>(x => x.Decorator.With<RecordingDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new DoThing(), CancellationToken.None)
        },
        {
            "AddCommandHandler<TCommand, TResponse, THandler>",
            s => s.AddCommandHandler<DoThingForValue, int, DoThingForValueHandler>(x => x.Decorator.With<RecordingDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new DoThingForValue(), CancellationToken.None)
        },
        {
            "AddQueryHandler",
            s => s.AddQueryHandler<GetValue, int, GetValueHandler>(x => x.Decorator.With<RecordingDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new GetValue(), CancellationToken.None)
        },
        {
            "AddNotificationHandler (handler entry)",
            s => s.AddNotificationHandler<Pinged>(x => x.Handler.Sequence.With<PingedHandler>(h => h.Decorator.With<RecordingDecorator>())),
            sp => sp.GetRequiredService<IPublisher>().PublishAsync(new Pinged(), CancellationToken.None)
        },
    };

    [Theory]
    [MemberData(nameof(DecoratedPipelines))]
    public async Task GivenUnregisteredDecorator_WhenAttachedViaDecoratorWith_ThenItIsRegisteredAsTransientAndRuns(
        string builder, Action<IServiceCollection> register, Func<IServiceProvider, Task> dispatch)
    {
        await new Scenario()
            .Given($"RecordingDecorator is only attached through {builder}'s Decorator.With, never registered by hand", () =>
            {
                var services = new ServiceCollection()
                    .AddSingleton<CallLog>()
                    .AddSingleton<NotificationLog>()
                    .AddSingleton<DecoratorLog>()
                    .AddSendr()
                    .AddSendrNotification();
                register(services);
                return services.BuildServiceProvider();
            })
            .When("The message is dispatched twice in the same scope", async provider =>
            {
                using var scope = provider.CreateScope();
                await dispatch(scope.ServiceProvider);
                await dispatch(scope.ServiceProvider);
                return provider.GetRequiredService<DecoratorLog>();
            })
            .Then("The decorator ran on both dispatches")
                .Equal("Two decorator runs were recorded", 2, log => log.Instances.Count)
            .And("Each dispatch got a new decorator instance")
                .Distinct("The two runs used different instances", log => log.Instances, ReferenceEqualityComparer.Instance)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(DecoratedPipelines))]
    public async Task GivenDecoratorPreRegisteredByUser_WhenAttachedViaDecoratorWith_ThenUserRegistrationIsKept(
        string builder, Action<IServiceCollection> register, Func<IServiceProvider, Task> dispatch)
    {
        await new Scenario()
            .Given<PreRegisteredDecorator>($"A RecordingDecorator instance registered as a singleton before {builder}'s Decorator.With", ctx =>
            {
                var services = new ServiceCollection()
                    .AddSingleton<CallLog>()
                    .AddSingleton<NotificationLog>()
                    .AddSingleton(ctx.Log)
                    .AddSingleton(ctx.UserInstance)
                    .AddSendr()
                    .AddSendrNotification();
                register(services);
                ctx.Provider = services.BuildServiceProvider();
            })
            .When("The message is dispatched", async ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                await dispatch(scope.ServiceProvider);
            })
            .Then("The user's instance is the only decorator that ran", ctx =>
                Assert.Same(ctx.UserInstance, Assert.Single(ctx.Log.Instances)))
            .RunAsync();
    }

    private static IEnumerable<(string Builder, Action<IServiceCollection> Register, Func<IServiceProvider, Task> Dispatch)> PipelinesDecoratedWith<TDecorator>()
        where TDecorator : IRequestDecorator, IRequestDecorator.WithResponse, IStreamRequestDecorator,
            ICommandDecorator, ICommandDecorator.WithResponse, IQueryDecorator, INotificationDecorator =>
    [
        ("AddRequestHandler<TRequest, THandler>",
            s => s.AddRequestHandler<Ping, PingHandler>(x => x.Decorator.With<TDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new Ping(), CancellationToken.None)),
        ("AddRequestHandler<TRequest, TResponse, THandler>",
            s => s.AddRequestHandler<PingForValue, int, PingForValueHandler>(x => x.Decorator.With<TDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new PingForValue(), CancellationToken.None)),
        ("AddStreamRequestHandler",
            s => s.AddStreamRequestHandler<CountUp, int, CountUpHandler>(x => x.Decorator.With<TDecorator>()),
            async sp => { await foreach (var _ in sp.GetRequiredService<IStreamSender>().SendStream(new CountUp(), CancellationToken.None)) { } }),
        ("AddCommandHandler<TCommand, THandler>",
            s => s.AddCommandHandler<DoThing, DoThingHandler>(x => x.Decorator.With<TDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new DoThing(), CancellationToken.None)),
        ("AddCommandHandler<TCommand, TResponse, THandler>",
            s => s.AddCommandHandler<DoThingForValue, int, DoThingForValueHandler>(x => x.Decorator.With<TDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new DoThingForValue(), CancellationToken.None)),
        ("AddQueryHandler",
            s => s.AddQueryHandler<GetValue, int, GetValueHandler>(x => x.Decorator.With<TDecorator>()),
            sp => sp.GetRequiredService<ISender>().SendAsync(new GetValue(), CancellationToken.None)),
        ("AddNotificationHandler (handler entry)",
            s => s.AddNotificationHandler<Pinged>(x => x.Handler.Sequence.With<PingedHandler>(h => h.Decorator.With<TDecorator>())),
            sp => sp.GetRequiredService<IPublisher>().PublishAsync(new Pinged(), CancellationToken.None)),
    ];

    public static TheoryData<string, string, Action<IServiceCollection>, Action<IServiceCollection>, Func<IServiceProvider, Task>> AbstractlyDecoratedPipelines
    {
        get
        {
            var data = new TheoryData<string, string, Action<IServiceCollection>, Action<IServiceCollection>, Func<IServiceProvider, Task>>();
            foreach (var (builder, register, dispatch) in PipelinesDecoratedWith<IAppDecorator>())
                data.Add(builder, "interface", register, s => s.AddTransient<IAppDecorator, AppDecorator>(), dispatch);
            foreach (var (builder, register, dispatch) in PipelinesDecoratedWith<AppDecoratorBase>())
                data.Add(builder, "abstract class", register, s => s.AddTransient<AppDecoratorBase, AppDecorator>(), dispatch);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AbstractlyDecoratedPipelines))]
    public async Task GivenInterfaceOrAbstractDecoratorRegisteredAfterAddHandler_WhenDispatched_ThenProviderBuildsAndRegisteredImplementationRuns(
        string builder, string abstraction, Action<IServiceCollection> register, Action<IServiceCollection> registerImplementation,
        Func<IServiceProvider, Task> dispatch)
    {
        var log = new DecoratorLog();

        await new Scenario()
            .Given($"{builder}'s Decorator.With an {abstraction}, whose implementation the application registers only afterwards", () =>
            {
                var services = new ServiceCollection()
                    .AddSingleton<CallLog>()
                    .AddSingleton<NotificationLog>()
                    .AddSingleton(log)
                    .AddSendr()
                    .AddSendrNotification();
                register(services);
                registerImplementation(services);
                return (Func<ServiceProvider>)(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));
            })
            .When("The provider is built with ValidateOnBuild and the message is dispatched", async build =>
            {
                using var provider = build();
                using var scope = provider.CreateScope();
                await dispatch(scope.ServiceProvider);
            })
            .Then("The application's implementation is the decorator that ran", _ =>
                Assert.IsType<AppDecorator>(Assert.Single(log.Instances)))
            .RunAsync();
    }

    public static TheoryData<string, Action<IServiceCollection>> NotificationGroups => new()
    {
        { "Sequence", s => s.AddNotificationHandler<Pinged>(x => x.Handler.Sequence.With<PingedHandler>()) },
        { "Parallel", s => s.AddNotificationHandler<Pinged>(x => x.Handler.Parallel.With<PingedHandler>()) },
    };

    [Theory]
    [MemberData(nameof(NotificationGroups))]
    public async Task GivenNotificationHandlerPreRegisteredByUser_WhenAddedViaAddNotificationHandler_ThenUserRegistrationIsKept(
        string group, Action<IServiceCollection> register)
    {
        await new Scenario()
            .Given<PreRegisteredNotificationHandler>($"A PingedHandler instance registered as a singleton before it is added to the {group} group", ctx =>
            {
                var services = new ServiceCollection()
                    .AddSingleton(ctx.Log)
                    .AddSingleton(ctx.UserInstance)
                    .AddSendrNotification();
                register(services);
                ctx.Provider = services.BuildServiceProvider();
            })
            .When("The notification is published", async ctx =>
            {
                using var scope = ctx.Provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(new Pinged(), CancellationToken.None);
            })
            .Then("The user's instance is the only handler that ran", ctx =>
                Assert.Same(ctx.UserInstance, Assert.Single(ctx.Log.Handlers)))
            .RunAsync();
    }

    // ---- Scenario contexts ----

    private sealed class SenderContext
    {
        public ServiceProvider Provider { get; set; } = null!;
        public ISender? Sender { get; set; }
        public IStreamSender? StreamSender { get; set; }
    }

    private sealed class PublisherContext
    {
        public ServiceProvider Provider { get; set; } = null!;
        public IPublisher? Publisher { get; set; }
    }

    private sealed class ScopedResolution<TService> where TService : notnull
    {
        public ServiceProvider Provider { get; set; } = null!;
        public TService? First { get; private set; }
        public TService? SecondInSameScope { get; private set; }
        public TService? InOtherScope { get; private set; }

        public void ResolveAcrossScopes()
        {
            using (var scope = Provider.CreateScope())
            {
                First = scope.ServiceProvider.GetRequiredService<TService>();
                SecondInSameScope = scope.ServiceProvider.GetRequiredService<TService>();
            }

            using (var other = Provider.CreateScope())
                InOtherScope = other.ServiceProvider.GetRequiredService<TService>();
        }
    }

    private sealed class PreRegisteredDecorator
    {
        public DecoratorLog Log { get; } = new();
        public RecordingDecorator UserInstance { get; }
        public ServiceProvider Provider { get; set; } = null!;

        public PreRegisteredDecorator() => UserInstance = new RecordingDecorator(Log);
    }

    private sealed class PreRegisteredNotificationHandler
    {
        public NotificationLog Log { get; } = new();
        public PingedHandler UserInstance { get; }
        public ServiceProvider Provider { get; set; } = null!;

        public PreRegisteredNotificationHandler() => UserInstance = new PingedHandler(Log);
    }
}

// ---- Messages and handlers ----

public sealed class CallLog
{
    public List<string> Calls { get; } = [];
}

public sealed record Ping : IRequest;

public sealed class PingHandler(CallLog log) : IRequestHandler<Ping>
{
    public Task HandleAsync(Ping request, CancellationToken cancellationToken)
    {
        log.Calls.Add(nameof(PingHandler));
        return Task.CompletedTask;
    }
}

public sealed record PingForValue : IRequest<int>;

public sealed class PingForValueHandler : IRequestHandler<PingForValue, int>
{
    public Task<int> HandleAsync(PingForValue request, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed record CountUp : IStreamRequest<int>;

public sealed class CountUpHandler : IStreamRequestHandler<CountUp, int>
{
    public async IAsyncEnumerable<int> HandleAsync(CountUp request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
    }
}

public sealed record DoThing : ICommand;

public sealed class DoThingHandler : ICommandHandler<DoThing>
{
    public Task HandleAsync(DoThing command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record DoThingForValue : ICommand<int>;

public sealed class DoThingForValueHandler : ICommandHandler<DoThingForValue, int>
{
    public Task<int> HandleAsync(DoThingForValue command, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed record GetValue : IQuery<int>;

public sealed class GetValueHandler : IQueryHandler<GetValue, int>
{
    public Task<int> HandleAsync(GetValue query, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed record Pinged : INotification;

public sealed class NotificationLog
{
    public List<object> Handlers { get; } = [];
}

public sealed class PingedHandler(NotificationLog log) : INotificationHandler<Pinged>
{
    public Task HandleAsync(Pinged notification, CancellationToken cancellationToken)
    {
        lock (log.Handlers)
            log.Handlers.Add(this);
        return Task.CompletedTask;
    }
}

public sealed record MultiRequest : IRequest<int>, IRequest<string>;

public sealed class MultiRequestIntHandler : IRequestHandler<MultiRequest, int>
{
    public Task<int> HandleAsync(MultiRequest request, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed class MultiRequestStringHandler : IRequestHandler<MultiRequest, string>
{
    public Task<string> HandleAsync(MultiRequest request, CancellationToken cancellationToken) => Task.FromResult("1");
}

public sealed record MultiCommand : ICommand<int>, ICommand<string>;

public sealed class MultiCommandIntHandler : ICommandHandler<MultiCommand, int>
{
    public Task<int> HandleAsync(MultiCommand command, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed class MultiCommandStringHandler : ICommandHandler<MultiCommand, string>
{
    public Task<string> HandleAsync(MultiCommand command, CancellationToken cancellationToken) => Task.FromResult("1");
}

public sealed record MultiQuery : IQuery<int>, IQuery<string>;

public sealed class MultiQueryIntHandler : IQueryHandler<MultiQuery, int>
{
    public Task<int> HandleAsync(MultiQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
}

public sealed class MultiQueryStringHandler : IQueryHandler<MultiQuery, string>
{
    public Task<string> HandleAsync(MultiQuery query, CancellationToken cancellationToken) => Task.FromResult("1");
}

public sealed record MultiStream : IStreamRequest<int>, IStreamRequest<string>;

public sealed class MultiStreamIntHandler : IStreamRequestHandler<MultiStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(MultiStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return 1;
    }
}

public sealed class MultiStreamStringHandler : IStreamRequestHandler<MultiStream, string>
{
    public async IAsyncEnumerable<string> HandleAsync(MultiStream request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return "1";
    }
}

// ---- Decorators ----

public sealed class DecoratorLog
{
    public List<object> Instances { get; } = [];
}

// Implements every decorator interface so one type can be attached through each of the seven builders.
public sealed class RecordingDecorator(DecoratorLog log) :
    IRequestDecorator,
    IRequestDecorator.WithResponse,
    IStreamRequestDecorator,
    ICommandDecorator,
    ICommandDecorator.WithResponse,
    IQueryDecorator,
    INotificationDecorator
{
    private void Record()
    {
        lock (log.Instances)
            log.Instances.Add(this);
    }

    public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        Record();
        return next();
    }

    Task<TResponse> IRequestDecorator.WithResponse.HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    IAsyncEnumerable<TResponse> IStreamRequestDecorator.HandleAsync<TRequest, TResponse>(
        TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task ICommandDecorator.HandleAsync<TCommand>(TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task<TResponse> ICommandDecorator.WithResponse.HandleAsync<TCommand, TResponse>(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task<TResponse> IQueryDecorator.HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task INotificationDecorator.HandleAsync<TNotification>(
        TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }
}

// A decorator contract an application implements itself; Decorator.With names the abstraction.
public interface IAppDecorator :
    IRequestDecorator,
    IRequestDecorator.WithResponse,
    IStreamRequestDecorator,
    ICommandDecorator,
    ICommandDecorator.WithResponse,
    IQueryDecorator,
    INotificationDecorator;

public abstract class AppDecoratorBase : IAppDecorator
{
    protected abstract void Record();

    public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        Record();
        return next();
    }

    Task<TResponse> IRequestDecorator.WithResponse.HandleAsync<TRequest, TResponse>(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    IAsyncEnumerable<TResponse> IStreamRequestDecorator.HandleAsync<TRequest, TResponse>(
        TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task ICommandDecorator.HandleAsync<TCommand>(TCommand command, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task<TResponse> ICommandDecorator.WithResponse.HandleAsync<TCommand, TResponse>(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task<TResponse> IQueryDecorator.HandleAsync<TQuery, TResponse>(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }

    Task INotificationDecorator.HandleAsync<TNotification>(
        TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        Record();
        return next();
    }
}

public sealed class AppDecorator(DecoratorLog log) : AppDecoratorBase
{
    protected override void Record()
    {
        lock (log.Instances)
            log.Instances.Add(this);
    }
}

// ---- Custom senders and publishers ----

public sealed class SenderDependency;

public sealed class CustomSender(SenderDependency dependency) : ISender
{
    public SenderDependency Dependency { get; } = dependency;

    public Task SendAsync(IRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SendAsync(ICommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> SendStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class DisposeProbe
{
    public int Constructed;
    public int Disposed;
}

public sealed class DisposableCustomSender : ISender, IDisposable
{
    private readonly DisposeProbe _probe;

    public DisposableCustomSender(DisposeProbe probe)
    {
        _probe = probe;
        Interlocked.Increment(ref probe.Constructed);
    }

    public void Dispose() => Interlocked.Increment(ref _probe.Disposed);

    public Task SendAsync(IRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SendAsync(ICommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> SendStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class CustomPublisher(SenderDependency dependency) : IPublisher
{
    public SenderDependency Dependency { get; } = dependency;

    public Task PublishAsync(INotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class DisposableCustomPublisher : IPublisher, IDisposable
{
    private readonly DisposeProbe _probe;

    public DisposableCustomPublisher(DisposeProbe probe)
    {
        _probe = probe;
        Interlocked.Increment(ref probe.Constructed);
    }

    public void Dispose() => Interlocked.Increment(ref _probe.Disposed);

    public Task PublishAsync(INotification notification, CancellationToken cancellationToken) => throw new NotSupportedException();
}
