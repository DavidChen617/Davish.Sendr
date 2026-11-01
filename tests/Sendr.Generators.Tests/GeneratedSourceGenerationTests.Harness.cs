using Davish.JustBdd;
using Microsoft.CodeAnalysis;

namespace Sendr.Generators.Tests;

// source-generation through GeneratorHarness: in-memory host and library compilations, for scenarios that would
// not compile in this project or need another assembly's shape. Only diagnostic ids, severities and messages are
// checked, never their locations.
public partial class GeneratedSourceGenerationTests
{
    /// <summary>A host compilation named "Host", with <paramref name="body"/> inside a method that has <c>services</c> in scope.</summary>
    private static string Host(string body, string declarations = "") => $$"""
        using Davish.Sendr;
        using Microsoft.Extensions.DependencyInjection;

        namespace Host;

        {{declarations}}

        public static class Wiring
        {
            public static void Configure(IServiceCollection services)
            {
                {{body}}
            }
        }
        """;

    private const string MarkerLibrary = """
        using Davish.Sendr;

        namespace Lib;

        public sealed class Marker;
        public sealed class OtherMarker;

        public sealed record LibQuery : IQuery<int>;

        public sealed class LibQueryHandler : IQueryHandler<LibQuery, int>
        {
            public Task<int> HandleAsync(LibQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
        }

        public sealed record LibNote : INotification;

        public sealed class LibNoteHandler : INotificationHandler<LibNote>
        {
            public Task HandleAsync(LibNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """;

    private static MetadataReference Library(string source, string name = "Lib", params MetadataReference[] references) =>
        GeneratorHarness.AsMetadata(GeneratorHarness.Compile(name, source, references));

    /// <summary>References <paramref name="source"/> as a live compilation, the way an IDE sees a project reference.</summary>
    private static MetadataReference LiveLibrary(string source, string name = "Lib") =>
        GeneratorHarness.Compile(name, source).ToMetadataReference();

    private static GeneratorRun RunHost(string body, string declarations = "", params MetadataReference[] references) =>
        GeneratorHarness.Run(GeneratorHarness.Compile("Host", Host(body, declarations), references));

    // ---- Discovery rules ----

    public static TheoryData<string> NonInstantiableShapes => new() { "abstract class", "interface" };

    [Theory]
    [MemberData(nameof(NonInstantiableShapes))]
    public void GivenAbstractClassOrInterfaceImplementingHandlerInterface_WhenGeneratorRuns_ThenItIsNotDiscoveredAndNoDiagnosticIsReported(string shape)
    {
        var handler = shape == "interface"
            ? "public interface IHiddenHandler : IRequestHandler<Hidden>;"
            : "public abstract class AbstractHiddenHandler : IRequestHandler<Hidden> { public abstract Task HandleAsync(Hidden request, CancellationToken cancellationToken); }";

        new Scenario()
            .Given($"A host whose only handler for Hidden is an {shape}, with no concrete subtype", () =>
                $"public sealed record Hidden : IRequest;\n{handler}")
            .When("The generator runs", declarations => RunHost("services.AddSendr(o => o.UseGenerators());", declarations))
            .Then("Nothing is reported, Hidden is not dispatched, and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.False(run.Dispatches("Host.Hidden"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    public static TheoryData<string> ValueTypeShapes => new() { "struct", "record struct" };

    [Theory]
    [MemberData(nameof(ValueTypeShapes))]
    public void GivenValueTypeHandler_WhenGeneratorRuns_ThenSendr004ErrorIsReportedAndItIsNotDiscovered(string shape)
    {
        new Scenario()
            .Given($"A host whose handler for Hidden is a {shape}", () => $$"""
                public sealed record Hidden : IRequest;
                public {{shape}} ValueHandler : IRequestHandler<Hidden>
                {
                    public Task HandleAsync(Hidden request, CancellationToken cancellationToken) => Task.CompletedTask;
                }
                """)
            .When("The generator runs", declarations => RunHost("services.AddSendr(o => o.UseGenerators());", declarations))
            .Then("SENDR004 is reported as an error naming the handler, and it is not dispatched", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR004"));
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Contains("ValueHandler", diagnostic.GetMessage());
                Assert.False(run.Dispatches("Host.ValueHandler"));
            })
            .Run();
    }

    [Fact]
    public void GivenOpenGenericHandler_WhenGeneratorRuns_ThenSendr003InfoIsReportedAndItIsSkipped()
    {
        new Scenario()
            .Given("A host whose handler for Hidden has a type parameter", () => """
                public sealed record Hidden : IRequest;
                public sealed class GenericHandler<T> : IRequestHandler<Hidden>
                {
                    public Task HandleAsync(Hidden request, CancellationToken cancellationToken) => Task.CompletedTask;
                }
                """)
            .When("The generator runs", declarations => RunHost("services.AddSendr(o => o.UseGenerators());", declarations))
            .Then("SENDR003 is reported as info naming the handler, it is skipped, and the output compiles", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR003"));
                Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
                Assert.Contains("GenericHandler", diagnostic.GetMessage());
                Assert.False(run.Dispatches("Host.GenericHandler"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    // ---- Duplicate handlers ----

    public static TheoryData<string> DuplicatePlacements => new() { "both in this compilation", "one here and one included", "two included assemblies" };

    [Theory]
    [MemberData(nameof(DuplicatePlacements))]
    public void GivenTwoHandlersForSameMessageAndResponse_WhenGeneratorRuns_ThenSendr002ErrorIsReported(string placement)
    {
        new Scenario()
            .Given($"Two different handlers for the same message and response, {placement}", () => placement switch
            {
                "both in this compilation" => RunHost("services.AddSendr(o => o.UseGenerators());", """
                    public sealed record Dup : IQuery<int>;
                    public sealed class FirstDupHandler : IQueryHandler<Dup, int> { public Task<int> HandleAsync(Dup query, CancellationToken cancellationToken) => Task.FromResult(1); }
                    public sealed class SecondDupHandler : IQueryHandler<Dup, int> { public Task<int> HandleAsync(Dup query, CancellationToken cancellationToken) => Task.FromResult(2); }
                    """),
                "one here and one included" => RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));", """
                    public sealed class SecondDupHandler : IQueryHandler<Lib.LibQuery, int> { public Task<int> HandleAsync(Lib.LibQuery query, CancellationToken cancellationToken) => Task.FromResult(2); }
                    """, Library(MarkerLibrary)),
                _ => TwoIncludedDuplicates(),
            })
            .When("The generator ran", run => run)
            .Then("SENDR002 is reported as an error naming the message and both handlers", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR002"));
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                var message = diagnostic.GetMessage();
                var (messageType, first, second) = placement switch
                {
                    "both in this compilation" => ("Dup", "FirstDupHandler", "SecondDupHandler"),
                    "one here and one included" => ("LibQuery", "LibQueryHandler", "SecondDupHandler"),
                    _ => ("SharedQuery", "HandlerA", "HandlerB"),
                };
                Assert.Contains(messageType, message);
                Assert.Contains(first, message);
                Assert.Contains(second, message);
            })
            .Run();
    }

    private static GeneratorRun TwoIncludedDuplicates()
    {
        var shared = GeneratorHarness.Compile("SharedMessages", "using Davish.Sendr;\nnamespace Shared;\npublic sealed record SharedQuery : IQuery<int>;");
        var sharedReference = GeneratorHarness.AsMetadata(shared);
        var a = Library("""
            using Davish.Sendr;
            namespace LibA;
            public sealed class MarkerA;
            public sealed class HandlerA : IQueryHandler<Shared.SharedQuery, int> { public Task<int> HandleAsync(Shared.SharedQuery query, CancellationToken cancellationToken) => Task.FromResult(1); }
            """, "LibA", sharedReference);
        var b = Library("""
            using Davish.Sendr;
            namespace LibB;
            public sealed class MarkerB;
            public sealed class HandlerB : IQueryHandler<Shared.SharedQuery, int> { public Task<int> HandleAsync(Shared.SharedQuery query, CancellationToken cancellationToken) => Task.FromResult(2); }
            """, "LibB", sharedReference);
        return RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<LibA.MarkerA>().IncludeAssemblyOf<LibB.MarkerB>()));", "", sharedReference, a, b);
    }

    public static TheoryData<string> TwoResponseFamilies => new() { "IRequest<T>", "ICommand<T>", "IQuery<T>", "IStreamRequest<T>" };

    [Theory]
    [MemberData(nameof(TwoResponseFamilies))]
    public void GivenMessageTypeWithTwoResponseTypesEachHandled_WhenGeneratorRuns_ThenNoDiagnosticIsReported(string family)
    {
        var declarations = family switch
        {
            "IRequest<T>" => """
                public sealed record Twin : IRequest<int>, IRequest<string>;
                public sealed class TwinInt : IRequestHandler<Twin, int> { public Task<int> HandleAsync(Twin request, CancellationToken cancellationToken) => Task.FromResult(1); }
                public sealed class TwinString : IRequestHandler<Twin, string> { public Task<string> HandleAsync(Twin request, CancellationToken cancellationToken) => Task.FromResult("1"); }
                """,
            "ICommand<T>" => """
                public sealed record Twin : ICommand<int>, ICommand<string>;
                public sealed class TwinInt : ICommandHandler<Twin, int> { public Task<int> HandleAsync(Twin command, CancellationToken cancellationToken) => Task.FromResult(1); }
                public sealed class TwinString : ICommandHandler<Twin, string> { public Task<string> HandleAsync(Twin command, CancellationToken cancellationToken) => Task.FromResult("1"); }
                """,
            "IQuery<T>" => """
                public sealed record Twin : IQuery<int>, IQuery<string>;
                public sealed class TwinInt : IQueryHandler<Twin, int> { public Task<int> HandleAsync(Twin query, CancellationToken cancellationToken) => Task.FromResult(1); }
                public sealed class TwinString : IQueryHandler<Twin, string> { public Task<string> HandleAsync(Twin query, CancellationToken cancellationToken) => Task.FromResult("1"); }
                """,
            _ => """
                public sealed record Twin : IStreamRequest<int>, IStreamRequest<string>;
                public sealed class TwinInt : IStreamRequestHandler<Twin, int> { public async IAsyncEnumerable<int> HandleAsync(Twin request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.Yield(); yield return 1; } }
                public sealed class TwinString : IStreamRequestHandler<Twin, string> { public async IAsyncEnumerable<string> HandleAsync(Twin request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.Yield(); yield return "1"; } }
                """,
        };

        new Scenario()
            .Given($"One {family} message type with a handler per response type", () => declarations)
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators());", d))
            .Then("Nothing is reported and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenNotificationWithSeveralHandlers_WhenGeneratorRuns_ThenNoDiagnosticIsReported()
    {
        new Scenario()
            .Given("One notification type with two handlers", () => """
                public sealed record Happened : INotification;
                public sealed class FirstListener : INotificationHandler<Happened> { public Task HandleAsync(Happened notification, CancellationToken cancellationToken) => Task.CompletedTask; }
                public sealed class SecondListener : INotificationHandler<Happened> { public Task HandleAsync(Happened notification, CancellationToken cancellationToken) => Task.CompletedTask; }
                """)
            .When("The generator runs", d => RunHost("services.AddSendrNotification(o => o.UseGenerators());", d))
            .Then("Nothing is reported and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenPartialHandlerWithBaseListOnSeveralDeclarations_WhenGeneratorRuns_ThenNoSendr002IsReported()
    {
        new Scenario()
            .Given("A partial handler class with two declarations that both have a base list", () => """
                public sealed record Split : IQuery<int>;
                public sealed partial class SplitHandler : IQueryHandler<Split, int>
                {
                    public Task<int> HandleAsync(Split query, CancellationToken cancellationToken) => Task.FromResult(1);
                }
                public sealed partial class SplitHandler : IDisposable
                {
                    public void Dispose() { }
                }
                """)
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators());", d))
            .Then("Nothing is reported and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    private static readonly IReadOnlyDictionary<string, string> PartialInvalidDecoratorShapes = new Dictionary<string, string>
    {
        ["a query handler"] = """
            public sealed class NotADecorator;
            public sealed record SplitQuery : IQuery<int>;
            [DecorateWith<NotADecorator>]
            public sealed partial class SplitQueryHandler : IQueryHandler<SplitQuery, int>
            {
                public Task<int> HandleAsync(SplitQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed partial class SplitQueryHandler : IDisposable
            {
                public void Dispose() { }
            }
            """,
        ["a notification handler"] = """
            public sealed class NotADecorator;
            public sealed record SplitNote : INotification;
            [DecorateWith<NotADecorator>]
            public sealed partial class SplitNoteHandler : INotificationHandler<SplitNote>
            {
                public Task HandleAsync(SplitNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public sealed partial class SplitNoteHandler : IDisposable
            {
                public void Dispose() { }
            }
            """,
    };

    public static TheoryData<string> PartialInvalidDecoratorShapeNames => new(PartialInvalidDecoratorShapes.Keys);

    [Theory]
    [MemberData(nameof(PartialInvalidDecoratorShapeNames))]
    public void GivenPartialHandlerWithInvalidDecoratorAndSeveralBaseLists_WhenGeneratorRuns_ThenSendr001IsReportedOnce(string shape)
    {
        new Scenario()
            .Given($"{shape} split over two declarations with base lists, whose [DecorateWith] names a type that is not a decorator", () => PartialInvalidDecoratorShapes[shape])
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators()); services.AddSendrNotification(o => o.UseGenerators());", d))
            .Then("SENDR001 is reported once", run => Assert.Single(run.Sendr("SENDR001")))
            .Run();
    }

    // ---- [DecorateWith] checks ----

    private const string PlainDecorators = """
        public sealed class D1 : IRequestDecorator
        {
            public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken) where TRequest : IRequest => next();
        }
        public sealed class D2 : IRequestDecorator
        {
            public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken) where TRequest : IRequest => next();
        }
        """;

    [Fact]
    public void GivenClassWithSeveralDecorateWithAttributes_WhenGeneratorRuns_ThenEachAttributeGetsSendr001()
    {
        new Scenario()
            .Given("A handler carrying [DecorateWith<D1>] and [DecorateWith<D1, D2>]", () => PlainDecorators + """

                public sealed record Twice : IRequest;
                [DecorateWith<D1>]
                [DecorateWith<D1, D2>]
                public sealed class TwiceHandler : IRequestHandler<Twice>
                {
                    public Task HandleAsync(Twice request, CancellationToken cancellationToken) => Task.CompletedTask;
                }
                """)
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators());", d))
            .Then("Each attribute gets its own SENDR001 error asking to combine them", run =>
            {
                var diagnostics = run.Sendr("SENDR001");
                Assert.Equal(2, diagnostics.Length);
                Assert.All(diagnostics, d =>
                {
                    Assert.Equal(DiagnosticSeverity.Error, d.Severity);
                    Assert.Contains("Combine them into a single attribute", d.GetMessage());
                });
            })
            .Run();
    }

    // For each handler kind: a message, a handler, and the decorator interface that kind needs.
    private static readonly IReadOnlyDictionary<string, (string Declarations, string RequiredInterface)> HandlerKinds = new Dictionary<string, (string, string)>
    {
        ["IRequestHandler<T>"] = ("""
            public sealed record KindMessage : IRequest;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : IRequestHandler<KindMessage> { public Task HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.CompletedTask; }
            """, "Davish.Sendr.IRequestDecorator"),
        ["IRequestHandler<T, R>"] = ("""
            public sealed record KindMessage : IRequest<int>;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : IRequestHandler<KindMessage, int> { public Task<int> HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.FromResult(1); }
            """, "Davish.Sendr.IRequestDecorator.WithResponse"),
        ["IStreamRequestHandler<T, R>"] = ("""
            public sealed record KindMessage : IStreamRequest<int>;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : IStreamRequestHandler<KindMessage, int> { public async IAsyncEnumerable<int> HandleAsync(KindMessage message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.Yield(); yield return 1; } }
            """, "Davish.Sendr.IStreamRequestDecorator"),
        ["ICommandHandler<T>"] = ("""
            public sealed record KindMessage : ICommand;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : ICommandHandler<KindMessage> { public Task HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.CompletedTask; }
            """, "Davish.Sendr.ICommandDecorator"),
        ["ICommandHandler<T, R>"] = ("""
            public sealed record KindMessage : ICommand<int>;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : ICommandHandler<KindMessage, int> { public Task<int> HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.FromResult(1); }
            """, "Davish.Sendr.ICommandDecorator.WithResponse"),
        ["IQueryHandler<T, R>"] = ("""
            public sealed record KindMessage : IQuery<int>;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : IQueryHandler<KindMessage, int> { public Task<int> HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.FromResult(1); }
            """, "Davish.Sendr.IQueryDecorator"),
        ["INotificationHandler<T>"] = ("""
            public sealed record KindMessage : INotification;
            [DecorateWith<WrongDecorator>]
            public sealed class KindHandler : INotificationHandler<KindMessage> { public Task HandleAsync(KindMessage message, CancellationToken cancellationToken) => Task.CompletedTask; }
            """, "Davish.Sendr.INotificationDecorator"),
    };

    public static TheoryData<string> HandlerKindNames => new(HandlerKinds.Keys);

    [Theory]
    [MemberData(nameof(HandlerKindNames))]
    public void GivenDecoratorMissingRequiredDecoratorInterface_WhenGeneratorRuns_ThenSendr001NamesRequiredInterface(string kind)
    {
        var (declarations, requiredInterface) = HandlerKinds[kind];

        new Scenario()
            .Given($"An {kind} handler decorated with WrongDecorator, which implements no decorator interface", () =>
                "public sealed class WrongDecorator;\n" + declarations)
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators()); services.AddSendrNotification(o => o.UseGenerators());", d))
            .Then("SENDR001 names the decorator, the handler and the interface it is missing", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR001"));
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                var message = diagnostic.GetMessage();
                Assert.Contains("WrongDecorator", message);
                Assert.Contains("KindHandler", message);
                Assert.Contains($"does not implement {requiredInterface},", message);
            })
            .Run();
    }

    // ---- Cross-assembly inclusion ----

    [Fact]
    public void GivenAssemblyIncludedOnlyOnSenderSide_WhenGeneratorRuns_ThenItsNotificationHandlersAreNotDiscovered()
    {
        new Scenario()
            .Given("A library with a request handler and a notification handler, included only by the Sender side", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost("""
                services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
                services.AddSendrNotification(o => o.UseGenerators());
                """, "", library))
            .Then("Only the request handler is discovered, and the output compiles", run =>
            {
                Assert.True(run.Dispatches("Lib.LibQueryHandler"));
                Assert.False(run.Dispatches("Lib.LibNoteHandler"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenAssemblyIncludedOnlyOnPublisherSide_WhenGeneratorRuns_ThenItsRequestHandlersAreNotDiscovered()
    {
        new Scenario()
            .Given("A library with a request handler and a notification handler, included only by the Publisher side", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost("""
                services.AddSendr(o => o.UseGenerators());
                services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
                """, "", library))
            .Then("Only the notification handler is discovered, and the output compiles", run =>
            {
                Assert.True(run.Dispatches("Lib.LibNoteHandler"));
                Assert.False(run.Dispatches("Lib.LibQueryHandler"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenReferencedAssemblyNotIncluded_WhenGeneratorRuns_ThenItsHandlersAreNotDiscoveredAndNoDiagnosticIsReported()
    {
        new Scenario()
            .Given("A referenced library with a public and an internal handler (no InternalsVisibleTo) that no UseGenerators includes", () =>
                Library(MarkerLibrary + """

                    public sealed record SecretQuery : IQuery<int>;
                    internal sealed class SecretQueryHandler : IQueryHandler<SecretQuery, int>
                    {
                        public Task<int> HandleAsync(SecretQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
                    }
                    """))
            .When("The generator runs", library => RunHost("services.AddSendr(o => o.UseGenerators());", "", library))
            .Then("Nothing is reported, none of its handlers are discovered, and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.False(run.Dispatches("Lib.LibQueryHandler"));
                Assert.False(run.Dispatches("Lib.SecretQueryHandler"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    public static TheoryData<string> MarkerForms => new() { "one call chaining both markers", "two calls with one marker each" };

    [Theory]
    [MemberData(nameof(MarkerForms))]
    public void GivenSeveralMarkersFromSameAssembly_WhenGeneratorRuns_ThenNoConflictIsReportedAndOutputCompilesClean(string form)
    {
        var body = form == "one call chaining both markers"
            ? "services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>().IncludeAssemblyOf<Lib.OtherMarker>()));"
            : """
              services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
              services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.OtherMarker>()));
              """;

        new Scenario()
            .Given($"Two marker types from the same library, in {form}", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost(body, "", library))
            .Then("Nothing is reported (no SENDR002, no SENDR006) and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenMarkerFromCurrentAssembly_WhenGeneratorRuns_ThenInclusionIsIgnored()
    {
        new Scenario()
            .Given("One UseGenerators call naming a marker from this assembly, and another with no inclusion", () => """
                services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<LocalMarker>()));
                services.AddSendr(o => o.UseGenerators());
                """)
            .When("The generator runs", body => RunHost(body, "public sealed class LocalMarker;"))
            .Then("Nothing is reported (no SENDR006) and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    private const string ProblemHandlersLibrary = """
        using Davish.Sendr;

        namespace Lib;

        public sealed class Marker;
        public sealed class NotADecorator;

        public sealed record HiddenNote : INotification;

        internal sealed class HiddenNoteHandler : INotificationHandler<HiddenNote>
        {
            public Task HandleAsync(HiddenNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed record DecoratedNote : INotification;

        [DecorateWith<NotADecorator>]
        public sealed class BadlyDecoratedNoteHandler : INotificationHandler<DecoratedNote>
        {
            public Task HandleAsync(DecoratedNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public sealed record HiddenQuery : IQuery<int>;

        internal sealed class HiddenQueryHandler : IQueryHandler<HiddenQuery, int>
        {
            public Task<int> HandleAsync(HiddenQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
        }

        public sealed record DecoratedQuery : IQuery<int>;

        [DecorateWith<NotADecorator>]
        public sealed class BadlyDecoratedQueryHandler : IQueryHandler<DecoratedQuery, int>
        {
            public Task<int> HandleAsync(DecoratedQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
        }

        public sealed record StructQuery : IQuery<int>;

        public struct StructQueryHandler : IQueryHandler<StructQuery, int>
        {
            public Task<int> HandleAsync(StructQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
        }
        """;

    private static readonly string[] ProblemNotificationHandlers = ["HiddenNoteHandler", "BadlyDecoratedNoteHandler"];
    private static readonly string[] ProblemQueryHandlers = ["HiddenQueryHandler", "BadlyDecoratedQueryHandler", "StructQueryHandler"];

    /// <summary>Which of the library's problem handlers the run's SENDR001/004/007 diagnostics name, one entry per diagnostic.</summary>
    private static string[] ProblemHandlersReported(GeneratorRun run) =>
    [
        .. run.Diagnostics
            .Where(d => d.Id is "SENDR001" or "SENDR004" or "SENDR007")
            .Select(d => ProblemNotificationHandlers.Concat(ProblemQueryHandlers).Single(name => d.GetMessage().Contains($"Lib.{name}'")))
            .Order(),
    ];

    public static TheoryData<string> OneSide => new() { "sender", "publisher" };

    [Theory]
    [MemberData(nameof(OneSide))]
    public void GivenProblemHandlersInAssemblyIncludedOnOneSide_WhenGeneratorRuns_ThenOnlyThatSidesDiagnosticsAreReported(string side)
    {
        var body = side == "sender"
            ? "services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>())); services.AddSendrNotification(o => o.UseGenerators());"
            : "services.AddSendr(o => o.UseGenerators()); services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));";
        var expected = side == "sender" ? ProblemQueryHandlers : ProblemNotificationHandlers;

        new Scenario()
            .Given("An included library with problem notification handlers and problem query handlers", () => Library(ProblemHandlersLibrary))
            .When($"Only the {side} side includes it and the generator runs", library => RunHost(body, "", library))
            .Then($"Only the {side} side's handlers are reported, once each", run =>
                Assert.Equal(expected.Order(), ProblemHandlersReported(run)))
            .Run();
    }

    [Fact]
    public void GivenProblemHandlersInAssemblyIncludedOnBothSides_WhenGeneratorRuns_ThenEveryDiagnosticIsReported()
    {
        new Scenario()
            .Given("An included library with problem notification handlers and problem query handlers", () => Library(ProblemHandlersLibrary))
            .When("Both sides include it and the generator runs", library => RunHost(
                "services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>())); " +
                "services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));", "", library))
            .Then("Every problem handler is reported, once each", run =>
                Assert.Equal(ProblemNotificationHandlers.Concat(ProblemQueryHandlers).Order(), ProblemHandlersReported(run)))
            .Run();
    }

    // ---- UseGenerators configuration syntax ----

    private static readonly IReadOnlyDictionary<string, (string Body, string Declarations)> UnsupportedConfigurations = new Dictionary<string, (string, string)>
    {
        ["a delegate variable"] = ("Action<GeneratedSenderOptions> configure = g => g.IncludeAssemblyOf<LocalMarker>(); services.AddSendr(o => o.UseGenerators(configure));", ""),
        ["a method group"] = ("services.AddSendr(o => o.UseGenerators(Holder.ConfigureSender));",
            "public static class Holder { public static void ConfigureSender(GeneratedSenderOptions g) { } }"),
        ["an anonymous method"] = ("services.AddSendr(o => o.UseGenerators(delegate (GeneratedSenderOptions g) { }));", ""),
        ["a parenthesized lambda with two parameters"] = ("services.AddSendr(o => o.UseGenerators((g, extra) => { }));", ""),
        ["a block with a non-expression statement"] = ("services.AddSendr(o => o.UseGenerators(g => { if (true) g.IncludeAssemblyOf<LocalMarker>(); }));", ""),
        ["a call chain not starting at the parameter"] = ("services.AddSendr(o => o.UseGenerators(g => Factory.Options().IncludeAssemblyOf<LocalMarker>()));",
            "public static class Factory { public static GeneratedSenderOptions Options() => null!; }"),
        ["an unrecognized method"] = ("services.AddSendr(o => o.UseGenerators(g => g.GetHashCode()));", ""),
        ["IncludeAssemblyOf without a type argument"] = ("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf()));", ""),
        ["a marker type that does not resolve"] = ("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<DoesNotExist>()));", ""),
        ["a delegate variable on NotificationOptions"] = ("Action<GeneratedNotificationRunOptions> configure = x => x.RunAs(NotificationRunMode.Parallel); services.AddSendrNotification(o => o.UseGenerators(configure));", ""),
    };

    public static TheoryData<string> UnsupportedConfigurationNames => new(UnsupportedConfigurations.Keys);

    [Theory]
    [MemberData(nameof(UnsupportedConfigurationNames))]
    public void GivenUnsupportedUseGeneratorsConfiguration_WhenGeneratorRuns_ThenSendr005ErrorIsReported(string configuration)
    {
        var (body, declarations) = UnsupportedConfigurations[configuration];

        new Scenario()
            .Given($"UseGenerators configured with {configuration}", () => (Body: body, Declarations: declarations + "\npublic sealed class LocalMarker;"))
            .When("The generator runs", host => RunHost(host.Body, host.Declarations))
            .Then("SENDR005 is reported as an error", run =>
                Assert.Contains(run.Sendr("SENDR005"), d => d.Severity == DiagnosticSeverity.Error))
            .Run();
    }

    public static TheoryData<string> RunAsForms => new() { "RunAs alone", "RunAs chained with IncludeAssemblyOf", "a block of two expression statements" };

    [Theory]
    [MemberData(nameof(RunAsForms))]
    public void GivenRunAsInsidePublisherConfigurationLambda_WhenGeneratorRuns_ThenNoDiagnosticIsReported(string form)
    {
        var configuration = form switch
        {
            "RunAs alone" => "x => x.RunAs(NotificationRunMode.Parallel)",
            "RunAs chained with IncludeAssemblyOf" => "x => x.RunAs(NotificationRunMode.Parallel).IncludeAssemblyOf<Lib.Marker>()",
            _ => "x => { x.RunAs(NotificationRunMode.Parallel); x.IncludeAssemblyOf<Lib.Marker>(); }",
        };

        new Scenario()
            .Given($"AddSendrNotification(o => o.UseGenerators({configuration}))", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost($"services.AddSendrNotification(o => o.UseGenerators({configuration}));", "", library))
            .Then("Nothing is reported and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    [Fact]
    public void GivenUseGeneratorsMethodOnUnrelatedType_WhenGeneratorRuns_ThenItIsIgnored()
    {
        new Scenario()
            .Given("Another type with its own UseGenerators method, called with a delegate variable", () => """
                public sealed class Unrelated
                {
                    public Unrelated UseGenerators(Action<int> configure) => this;
                }
                """)
            .When("The generator runs", declarations => RunHost("Action<int> configure = _ => { }; new Unrelated().UseGenerators(configure);", declarations))
            .Then("No SENDR diagnostic is reported", run =>
                Assert.DoesNotContain(run.Diagnostics, d => d.Id.StartsWith("SENDR")))
            .Run();
    }

    public static TheoryData<string> Sides => new() { "Sender", "Publisher" };

    [Theory]
    [MemberData(nameof(Sides))]
    public void GivenSameSideUseGeneratorsCallsWithDifferentInclusionSets_WhenGeneratorRuns_ThenSendr006IsReportedAtBothCalls(string side)
    {
        var body = side == "Sender"
            ? """
              services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
              services.AddSendr(o => o.UseGenerators());
              """
            : """
              services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
              services.AddSendrNotification(o => o.UseGenerators());
              """;

        new Scenario()
            .Given($"Two {side} UseGenerators calls, one including a library and one not", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost(body, "", library))
            .Then($"Each call gets a SENDR006 error naming the {side} side", run =>
            {
                var diagnostics = run.Sendr("SENDR006");
                Assert.Equal(2, diagnostics.Length);
                Assert.All(diagnostics, d =>
                {
                    Assert.Equal(DiagnosticSeverity.Error, d.Severity);
                    Assert.Contains(side, d.GetMessage());
                });
            })
            .Run();
    }

    [Fact]
    public void GivenSenderAndPublisherDeclareDifferentInclusionSets_WhenGeneratorRuns_ThenNoSendr006IsReported()
    {
        new Scenario()
            .Given("The Sender side includes a library and the Publisher side includes nothing", () => Library(MarkerLibrary))
            .When("The generator runs", library => RunHost("""
                services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
                services.AddSendrNotification(o => o.UseGenerators());
                """, "", library))
            .Then("No SENDR006 is reported and the output compiles", run =>
            {
                Assert.Empty(run.Sendr("SENDR006"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    // ---- Accessibility of included types ----

    public static TheoryData<string> InaccessibleShapes => new() { "an internal handler", "a public handler with an internal decorator" };

    [Theory]
    [MemberData(nameof(InaccessibleShapes))]
    public void GivenInaccessibleHandlerOrDecoratorInIncludedAssembly_WhenGeneratorRuns_ThenSendr007IsReported(string shape)
    {
        var (source, inaccessible) = shape == "an internal handler"
            ? ("""
               using Davish.Sendr;
               namespace Lib;
               public sealed class Marker;
               public sealed record SecretQuery : IQuery<int>;
               internal sealed class SecretQueryHandler : IQueryHandler<SecretQuery, int>
               {
                   public Task<int> HandleAsync(SecretQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
               }
               """, "SecretQueryHandler")
            : ("""
               using Davish.Sendr;
               namespace Lib;
               public sealed class Marker;
               internal sealed class SecretDecorator : IQueryDecorator
               {
                   public Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
                       where TQuery : IQuery<TResponse> => next();
               }
               public sealed record OpenQuery : IQuery<int>;
               [DecorateWith<SecretDecorator>]
               public sealed class OpenQueryHandler : IQueryHandler<OpenQuery, int>
               {
                   public Task<int> HandleAsync(OpenQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
               }
               """, "SecretDecorator");

        new Scenario()
            .Given($"An included library with {shape}, and no InternalsVisibleTo for the host", () => Library(source))
            .When("The generator runs", library => RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));", "", library))
            .Then("SENDR007 is reported as an error naming the inaccessible type", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR007"));
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Contains(inaccessible, diagnostic.GetMessage());
                Assert.Contains("included assembly", diagnostic.GetMessage());
            })
            .Run();
    }

    private static readonly IReadOnlyDictionary<string, (string Declarations, string Inaccessible)> LocalInaccessibleShapes = new Dictionary<string, (string, string)>
    {
        ["a private nested handler"] = ("""
            public sealed record HiddenQuery : IQuery<int>;
            public static class Outer
            {
                private sealed class HiddenQueryHandler : IQueryHandler<HiddenQuery, int>
                {
                    public Task<int> HandleAsync(HiddenQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
                }
            }
            """, "HiddenQueryHandler"),
        ["a protected nested handler"] = ("""
            public sealed record ShieldedQuery : IQuery<int>;
            public class Outer
            {
                protected sealed class ShieldedQueryHandler : IQueryHandler<ShieldedQuery, int>
                {
                    public Task<int> HandleAsync(ShieldedQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
                }
            }
            """, "ShieldedQueryHandler"),
        ["a public handler with a private nested decorator"] = ("""
            public static class Outer
            {
                private sealed class SecretDecorator : IQueryDecorator
                {
                    public Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
                        where TQuery : IQuery<TResponse> => next();
                }

                public sealed record OpenQuery : IQuery<int>;

                [DecorateWith<SecretDecorator>]
                public sealed class OpenQueryHandler : IQueryHandler<OpenQuery, int>
                {
                    public Task<int> HandleAsync(OpenQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
                }
            }
            """, "SecretDecorator"),
    };

    public static TheoryData<string> LocalInaccessibleShapeNames => new(LocalInaccessibleShapes.Keys);

    [Theory]
    [MemberData(nameof(LocalInaccessibleShapeNames))]
    public void GivenInaccessibleNestedHandlerOrDecoratorInThisCompilation_WhenGeneratorRuns_ThenSendr007SaysItIsDeclaredInThisProject(string shape)
    {
        var (declarations, inaccessible) = LocalInaccessibleShapes[shape];

        new Scenario()
            .Given($"This compilation declares {shape}, which the generated code cannot reach", () => declarations)
            .When("The generator runs", d => RunHost("services.AddSendr(o => o.UseGenerators());", d))
            .Then("SENDR007 says the type is declared in this project, without pointing at included assemblies", run =>
            {
                var diagnostic = Assert.Single(run.Sendr("SENDR007"));
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                var message = diagnostic.GetMessage();
                Assert.Contains(inaccessible, message);
                Assert.Contains("declared in this project", message);
                Assert.DoesNotContain("included assembly", message);
                Assert.DoesNotContain("InternalsVisibleTo", message);
            })
            .Run();
    }

    [Fact]
    public void GivenInternalHandlerWithInternalsVisibleToHost_WhenGeneratorRuns_ThenItIsDiscoveredAndOutputCompilesClean()
    {
        new Scenario()
            .Given("An included library whose internal handler is visible to the host through InternalsVisibleTo", () => Library("""
                using Davish.Sendr;
                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Host")]
                namespace Lib;
                public sealed class Marker;
                public sealed record FriendQuery : IQuery<int>;
                internal sealed class FriendQueryHandler : IQueryHandler<FriendQuery, int>
                {
                    public Task<int> HandleAsync(FriendQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
                }
                """))
            .When("The generator runs", library => RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));", "", library))
            .Then("Nothing is reported, the handler is discovered, and the output compiles", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.True(run.Dispatches("Lib.FriendQueryHandler"));
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    // ---- Included assemblies referenced as compilations ----

    private static readonly IReadOnlyDictionary<string, (string Source, string Id, string[] Named)> LiveLibraryProblems = new Dictionary<string, (string, string, string[])>
    {
        ["a decorator missing the required interface"] = ("""
            using Davish.Sendr;
            namespace Lib;
            public sealed class Marker;
            public sealed class NotADecorator;
            public sealed record BadQuery : IQuery<int>;
            [DecorateWith<NotADecorator>]
            public sealed class BadQueryHandler : IQueryHandler<BadQuery, int>
            {
                public Task<int> HandleAsync(BadQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """, "SENDR001", ["NotADecorator"]),
        ["two [DecorateWith] attributes"] = ("""
            using Davish.Sendr;
            namespace Lib;
            public sealed class Marker;
            public sealed class PassDecorator : IQueryDecorator
            {
                public Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
                    where TQuery : IQuery<TResponse> => next();
            }
            public sealed record TwiceQuery : IQuery<int>;
            [DecorateWith<PassDecorator>]
            [DecorateWith<PassDecorator, PassDecorator>]
            public sealed class TwiceQueryHandler : IQueryHandler<TwiceQuery, int>
            {
                public Task<int> HandleAsync(TwiceQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """, "SENDR001", ["TwiceQueryHandler", "TwiceQueryHandler"]),
        ["an internal decorator"] = ("""
            using Davish.Sendr;
            namespace Lib;
            public sealed class Marker;
            internal sealed class SecretDecorator : IQueryDecorator
            {
                public Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
                    where TQuery : IQuery<TResponse> => next();
            }
            public sealed record OpenQuery : IQuery<int>;
            [DecorateWith<SecretDecorator>]
            public sealed class OpenQueryHandler : IQueryHandler<OpenQuery, int>
            {
                public Task<int> HandleAsync(OpenQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """, "SENDR007", ["SecretDecorator"]),
        ["an internal handler"] = ("""
            using Davish.Sendr;
            namespace Lib;
            public sealed class Marker;
            public sealed record SecretQuery : IQuery<int>;
            internal sealed class SecretQueryHandler : IQueryHandler<SecretQuery, int>
            {
                public Task<int> HandleAsync(SecretQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """, "SENDR007", ["SecretQueryHandler"]),
    };

    public static TheoryData<string> LiveLibraryProblemNames => new(LiveLibraryProblems.Keys);

    [Theory]
    [MemberData(nameof(LiveLibraryProblemNames))]
    public void GivenDecoratorProblemInAssemblyIncludedAsCompilationReference_WhenGeneratorRuns_ThenItIsReportedAndGenerationSucceeds(string problem)
    {
        var (source, id, named) = LiveLibraryProblems[problem];

        new Scenario()
            .Given($"An included library with {problem}, referenced as a live compilation rather than a built assembly", () => LiveLibrary(source))
            .When("The generator runs", library => RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));", "", library))
            .Then("The generator did not fail, the problem is reported as an error, and UseGenerators is still generated", run =>
            {
                Assert.Empty(run.Diagnostics.Where(d => d.Id == "CS8785"));
                var reported = run.Sendr(id);
                Assert.Equal(named.Length, reported.Length);
                Assert.All(reported, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
                Assert.All(named.Zip(reported), pair => Assert.Contains(pair.First, pair.Second.GetMessage()));
                Assert.Contains("UseGenerators(", run.GeneratedSource);
            })
            .Run();
    }

    // ---- Davish.Sendr not referenced ----

    private static readonly IReadOnlyDictionary<string, string> AbstractionsOnlyProjects = new Dictionary<string, string>
    {
        ["no handlers"] = "namespace Lib; public sealed class Nothing;",
        ["a handler"] = """
            using Davish.Sendr;
            namespace Lib;
            public sealed record LibQuery : IQuery<int>;
            public sealed class LibQueryHandler : IQueryHandler<LibQuery, int>
            {
                public Task<int> HandleAsync(LibQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed record LibNote : INotification;
            public sealed class LibNoteHandler : INotificationHandler<LibNote>
            {
                public Task HandleAsync(LibNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
        ["a struct handler"] = """
            using Davish.Sendr;
            namespace Lib;
            public sealed record StructQuery : IQuery<int>;
            public struct StructQueryHandler : IQueryHandler<StructQuery, int>
            {
                public Task<int> HandleAsync(StructQuery query, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """,
    };

    public static TheoryData<string> AbstractionsOnlyProjectNames => new(AbstractionsOnlyProjects.Keys);

    [Theory]
    [MemberData(nameof(AbstractionsOnlyProjectNames))]
    public void GivenProjectReferencingAbstractionsButNotSendr_WhenGeneratorRuns_ThenNothingIsGeneratedAndSendr008IsReported(string contents)
    {
        new Scenario()
            .Given($"A project that references Davish.Sendr.Abstractions and the generator but not Davish.Sendr, with {contents}", () =>
                GeneratorHarness.CompileWithoutSendr("Lib", AbstractionsOnlyProjects[contents]))
            .When("The generator runs", GeneratorHarness.Run)
            .Then("Nothing is generated, SENDR008 is the only generator diagnostic, and the project compiles", run =>
            {
                Assert.Equal("", run.GeneratedSource);
                var diagnostic = Assert.Single(run.Diagnostics);
                Assert.Equal("SENDR008", diagnostic.Id);
                Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
                Assert.Contains("Davish.Sendr is not", diagnostic.GetMessage());
                Assert.Contains("IncludeAssemblyOf", diagnostic.GetMessage());
                Assert.Empty(run.CompileErrors);
            })
            .Run();
    }

    // ---- Generated code compiles cleanly ----

    private const string EveryKindHost = """
        public sealed record R1 : IRequest;
        public sealed record R2 : IRequest<int>;
        public sealed record S1 : IStreamRequest<int>;
        public sealed record C1 : ICommand;
        public sealed record C2 : ICommand<int>;
        public sealed record Q1 : IQuery<int>, IQuery<string>;
        public sealed record N1 : INotification;

        public interface IAudit : IRequestDecorator;
        public sealed class Audit : IAudit
        {
            public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken) where TRequest : IRequest => next();
        }
        public sealed class Log : IRequestDecorator, IQueryDecorator
        {
            public Task HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken) where TRequest : IRequest => next();
            public Task<TResponse> HandleAsync<TQuery, TResponse>(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) where TQuery : IQuery<TResponse> => next();
        }

        [DecorateWith<Log, IAudit>]
        public sealed class R1Handler : IRequestHandler<R1> { public Task HandleAsync(R1 request, CancellationToken cancellationToken) => Task.CompletedTask; }
        public sealed class R2Handler : IRequestHandler<R2, int> { public Task<int> HandleAsync(R2 request, CancellationToken cancellationToken) => Task.FromResult(1); }
        public sealed class S1Handler : IStreamRequestHandler<S1, int> { public async IAsyncEnumerable<int> HandleAsync(S1 request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.Yield(); yield return 1; } }
        public sealed class C1Handler : ICommandHandler<C1> { public Task HandleAsync(C1 command, CancellationToken cancellationToken) => Task.CompletedTask; }
        public sealed class C2Handler : ICommandHandler<C2, int> { public Task<int> HandleAsync(C2 command, CancellationToken cancellationToken) => Task.FromResult(1); }
        [DecorateWith<Log>]
        public sealed class Q1IntHandler : IQueryHandler<Q1, int> { public Task<int> HandleAsync(Q1 query, CancellationToken cancellationToken) => Task.FromResult(1); }
        public sealed class Q1StringHandler : IQueryHandler<Q1, string> { public Task<string> HandleAsync(Q1 query, CancellationToken cancellationToken) => Task.FromResult("1"); }
        public sealed class N1Handler : INotificationHandler<N1> { public Task HandleAsync(N1 notification, CancellationToken cancellationToken) => Task.CompletedTask; }
        """;

    public static TheoryData<string> CleanHosts => new() { "no handlers", "every handler kind with decorators", "an included library" };

    [Theory]
    [MemberData(nameof(CleanHosts))]
    public void GivenHostCompilation_WhenGeneratorOutputIsCompiledWithIt_ThenThereAreNoErrorsOrWarnings(string host)
    {
        new Scenario()
            .Given($"A host with {host}, referencing Sendr and Sendr.Abstractions", () => host)
            .When("The generator runs and its output is compiled with the host", h => h switch
            {
                "no handlers" => RunHost("services.AddSendr(o => o.UseGenerators()); services.AddSendrNotification(o => o.UseGenerators());"),
                "every handler kind with decorators" => RunHost("services.AddSendr(o => o.UseGenerators()); services.AddSendrNotification(o => o.UseGenerators());", EveryKindHost),
                _ => RunHost("services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>())); services.AddSendrNotification(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));",
                    "", Library(MarkerLibrary)),
            })
            .Then("The generator reports nothing, and the compilation has no errors or warnings", run =>
            {
                Assert.Empty(run.Diagnostics);
                Assert.Empty(run.CompileProblems);
            })
            .Run();
    }
}
