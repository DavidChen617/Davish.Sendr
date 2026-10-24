using Davish.JustBdd;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;
using Sendr.TestFeatures;
using Sendr.TestFixtures;

namespace Sendr.Tests;

/// <summary>decorators through the default reflection-based sender and publisher.</summary>
[Feature<DecoratorsFeature>]
public class DecoratorsTests : DecoratorsSpec
{
    protected override IServiceCollection AddSenderAndPublisherUnderTest(IServiceCollection services) =>
        services.AddSendr().AddSendrNotification().AddPipelineHandlersManually();

    public static TheoryData<string> Groups => new() { "Sequence", "Parallel" };

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenDecoratorOnOneNotificationHandler_WhenPublish_ThenOnlyThatHandlerIsWrapped(string group)
    {
        await new Scenario()
            .Given<PipelineContext>($"PartialNote has two handlers in the {group} group and only one gets h.Decorator.With<SingleDecorator>()", ctx =>
                ctx.Build(services => services.AddSendrNotification().AddNotificationHandler<PartialNote>(x =>
                {
                    if (group == "Sequence")
                        x.Handler.Sequence.With<PartialDecoratedHandler>(h => h.Decorator.With<SingleDecorator>()).With<PartialPlainHandler>();
                    else
                        x.Handler.Parallel.With<PartialDecoratedHandler>(h => h.Decorator.With<SingleDecorator>()).With<PartialPlainHandler>();
                })))
            .When("PartialNote is published and awaited", ctx =>
                ctx.Scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(new PartialNote(), CancellationToken.None))
            .Then("Only the decorated handler was wrapped, and the plain one still ran", ctx => AssertOnlyDecoratedHandlerWasWrapped(ctx.Probe))
            .RunAsync();
    }

    public static TheoryData<string> RetainedOptionTypes => new(RetainedOptions.Keys);

    // For each options type: register Pair's handler with decorator A while keeping the options reference, and
    // return the late step that pushes decorator B through that kept reference after BuildServiceProvider().
    private static readonly IReadOnlyDictionary<string, (string Kind, Func<IServiceCollection, Action> Register)> RetainedOptions =
        new Dictionary<string, (string, Func<IServiceCollection, Action>)>
        {
            ["RequestHandlerOptions<TRequest>"] = ("SendAsync(IRequest)", services =>
            {
                RequestHandlerOptions<PairRequest>? kept = null;
                services.AddRequestHandler<PairRequest, PairRequestHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["RequestHandlerOptions<TRequest, TResponse>"] = ("SendAsync(IRequest<T>)", services =>
            {
                RequestHandlerOptions<PairRequestForValue, int>? kept = null;
                services.AddRequestHandler<PairRequestForValue, int, PairRequestForValueHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["CommandHandlerOptions<TCommand>"] = ("SendAsync(ICommand)", services =>
            {
                CommandHandlerOptions<PairCommand>? kept = null;
                services.AddCommandHandler<PairCommand, PairCommandHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["CommandHandlerOptions<TCommand, TResponse>"] = ("SendAsync(ICommand<T>)", services =>
            {
                CommandHandlerOptions<PairCommandForValue, int>? kept = null;
                services.AddCommandHandler<PairCommandForValue, int, PairCommandForValueHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["QueryHandlerOptions"] = ("SendAsync(IQuery<T>)", services =>
            {
                QueryHandlerOptions<PairQuery, int>? kept = null;
                services.AddQueryHandler<PairQuery, int, PairQueryHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["StreamRequestHandlerOptions"] = (PipelineCases.SendStream, services =>
            {
                StreamRequestHandlerOptions<PairStream, int>? kept = null;
                services.AddStreamRequestHandler<PairStream, int, PairStreamHandler>(x => { kept = x; x.Decorator.With<OuterDecorator>(); });
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
            ["NotificationHandlerEntryOptions"] = (PipelineCases.PublishAsync, services =>
            {
                NotificationHandlerEntryOptions<PairNote>? kept = null;
                services.AddNotificationHandler<PairNote>(x => x.Handler.Sequence.With<PairNoteHandler>(h => { kept = h; h.Decorator.With<OuterDecorator>(); }));
                return () => kept!.Decorator.With<InnerDecorator>();
            }
            ),
        };

    [Theory]
    [MemberData(nameof(RetainedOptionTypes))]
    public async Task GivenRetainedHandlerOptions_WhenDecoratorAddedAfterBuildServiceProvider_ThenBuiltPipelineIsUnaffected(string optionsType)
    {
        var (kind, register) = RetainedOptions[optionsType];
        Action addLateDecorator = null!;

        await new Scenario()
            .Given<PipelineContext>($"A handler registered with decorator A while the caller keeps its {optionsType}, and the provider already built", ctx =>
                ctx.Build(services =>
                {
                    addLateDecorator = register(services.AddSendr().AddSendrNotification());
                    return services;
                }))
            .When("Decorator B is added through the kept options, then the built provider dispatches", async ctx =>
            {
                addLateDecorator();
                await PipelineCases.Pair[kind](ctx.Scope.ServiceProvider);
            })
            .Then("Only the decorator declared at registration ran")
                .Equal("A wrapped the handler and B never ran", ["A before", .. PipelineCases.HandlerEntries(kind), "A after"], ctx => ctx.Probe.Log)
            .RunAsync();
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task GivenRetainedNotificationHandlerOptions_WhenHandlerAddedAfterBuildServiceProvider_ThenBuiltPipelineIsUnaffected(string group)
    {
        NotificationHandlerOptions<PartialNote> kept = null!;

        await new Scenario()
            .Given<PipelineContext>("PartialNote registered with only its plain handler while the caller keeps the NotificationHandlerOptions, and the provider already built", ctx =>
                ctx.Build(services => services.AddSendrNotification().AddNotificationHandler<PartialNote>(x =>
                {
                    kept = x;
                    x.Handler.Sequence.With<PartialPlainHandler>();
                })))
            .When($"Another handler is added to the {group} group through the kept options, then the built provider publishes", ctx =>
            {
                if (group == "Sequence")
                    kept.Handler.Sequence.With<PartialDecoratedHandler>();
                else
                    kept.Handler.Parallel.With<PartialDecoratedHandler>();
                return ctx.Scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(new PartialNote(), CancellationToken.None);
            })
            .Then("Only the handler declared at registration ran")
                .Equal("Just the plain handler ran", ["plain handler"], ctx => ctx.Probe.Log)
            .RunAsync();
    }
}
