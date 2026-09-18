using System.Collections.Immutable;
using Davish.Sendr;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Generator.Tests;

/// <summary>
/// Drives <see cref="SendrSourceGenerator"/> directly against small, independently constructed
/// in-memory compilations via <see cref="GeneratorDriver"/> — unlike the rest of this project's
/// tests, which exercise the generator only as a real analyzer against this project's own single,
/// shared compilation (and so can't represent two differently configured "apps", per SENDR006).
/// This is how cross-assembly discovery's negative/diagnostic paths are covered: each test builds
/// its own host (and, where needed, library) compilation from scratch, so one test's configuration
/// never leaks into another's.
/// </summary>
public class GeneratorHarnessTests
{
    private static ImmutableArray<MetadataReference>? _baseReferences;

    // The standard "self-hosted Roslyn generator test" trick: the running test process's own
    // trusted platform assembly list already contains every dependency this project itself needed
    // to build and run (BCL, Microsoft.Extensions.DependencyInjection.Abstractions, and — since
    // Generator.Tests references them — Davish.Sendr, Davish.Sendr.Abstractions, Davish.Sendr.Shared
    // too), so there's no need to hand-list assembly locations one by one.
    private static ImmutableArray<MetadataReference> GetBaseReferences()
    {
        if (_baseReferences is { } cached)
            return cached;

        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        // Excludes this very test assembly: Generator.Tests.dll already has Sendr.Generators'
        // *real* generated output baked into its own IL (it references Sendr.Generators as an
        // actual analyzer, exercised by the rest of this project's tests), so including it here
        // would give every harness compilation a second, colliding copy of e.g.
        // SendrOptionsGeneratedExtensions.UseGenerators(...) — ambiguous against the one this
        // harness's own GeneratorDriver run produces fresh.
        var ownAssemblyLocation = typeof(GeneratorHarnessTests).Assembly.Location;

        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => File.Exists(path) && path != ownAssemblyLocation)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        _baseReferences = references;
        return references;
    }

    // Mirrors what <ImplicitUsings>enable</ImplicitUsings> generates for a real project — needed
    // here because these synthetic compilations have no such project-level global usings file, and
    // SourceBuilder's generated code (e.g. the stream sender's `.WithCancellation(...)` call) relies
    // on System.Threading.Tasks being in scope compilation-wide, the same as it is in every real
    // Sendr project.
    private const string GlobalUsingsSource = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static CSharpCompilation CreateCompilation(
        string assemblyName, string source, params MetadataReference[] extraReferences)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var globalUsingsTree = CSharpSyntaxTree.ParseText(GlobalUsingsSource, parseOptions);
        return CSharpCompilation.Create(
            assemblyName,
            [tree, globalUsingsTree],
            GetBaseReferences().AddRange(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(CSharpCompilation hostCompilation)
    {
        // The driver must parse its own generated sources with the same CSharpParseOptions as the
        // host's existing trees — RunGeneratorsAndUpdateCompilation adds them via AddSyntaxTrees,
        // which throws ArgumentException("Inconsistent language versions") otherwise.
        var parseOptions = (CSharpParseOptions)hostCompilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SendrSourceGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(hostCompilation, out var outputCompilation, out _);
        var runResult = driver.GetRunResult();
        return (outputCompilation, runResult.Diagnostics);
    }

    private static ImmutableArray<Diagnostic> CompileErrors(Compilation compilation) =>
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();

    private const string LibrarySource = """
        using Davish.Sendr;

        namespace Lib;

        public sealed class Marker;
        public sealed class OtherMarker;

        public sealed record LibRequest : IQuery<LibResponse>;
        public sealed record LibResponse;

        public sealed class LibRequestHandler : IQueryHandler<LibRequest, LibResponse>
        {
            public Task<LibResponse> HandleAsync(LibRequest query, CancellationToken cancellationToken)
                => Task.FromResult(new LibResponse());
        }
        """;

    private const string HostUsingsAndWiringPrefix = """
        using Davish.Sendr;
        using Microsoft.Extensions.DependencyInjection;

        public static class Wiring
        {
            public static void Configure(IServiceCollection services)
            {
        """;

    private const string HostSuffix = """
            }
        }
        """;

    [Fact]
    public void GivenNoInclusion_WhenGeneratorRuns_ThenExternalRequestTypeIsNotInGeneratedDispatch()
    {
        // Given
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators());\n" +
                          HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (output, diagnostics) = RunGenerator(host);

        // Then
        Assert.Empty(diagnostics);
        Assert.Empty(CompileErrors(output));
        var generated = output.SyntaxTrees.Select(t => t.ToString()).Single(t => t.Contains("GeneratedSender"));
        Assert.DoesNotContain("Lib.LibRequest", generated);
    }

    [Fact]
    public void GivenInclusionDeclared_WhenGeneratorRuns_ThenExternalRequestTypeIsDiscoveredAndCompilesClean()
    {
        // Given
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));\n" +
                          HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (output, diagnostics) = RunGenerator(host);

        // Then
        Assert.Empty(diagnostics);
        Assert.Empty(CompileErrors(output));
        var generated = output.SyntaxTrees.Select(t => t.ToString()).Single(t => t.Contains("GeneratedSender"));
        Assert.Contains("Lib.LibRequest", generated);
        Assert.Contains("Lib.LibRequestHandler", generated);
    }

    [Fact]
    public void GivenDuplicateMarkersForSameAssembly_WhenGeneratorRuns_ThenNoConflictAndCompilesClean()
    {
        // Given — Marker and OtherMarker are declared in the same library assembly, so this must
        // not be treated as two different assemblies worth of the same handler colliding.
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>().IncludeAssemblyOf<Lib.OtherMarker>()));\n" +
                          HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (output, diagnostics) = RunGenerator(host);

        // Then
        Assert.Empty(diagnostics);
        Assert.Empty(CompileErrors(output));
    }

    [Fact]
    public void GivenLocalAndExternalHandlerForSameRequest_WhenGeneratorRuns_ThenSendr002Reported()
    {
        // Given
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = """
            using Davish.Sendr;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class LocalConflictingHandler : IQueryHandler<Lib.LibRequest, Lib.LibResponse>
            {
                public Task<Lib.LibResponse> HandleAsync(Lib.LibRequest query, CancellationToken cancellationToken)
                    => Task.FromResult(new Lib.LibResponse());
            }

            """ + HostUsingsAndWiringPrefix +
            "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));\n" +
            HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (_, diagnostics) = RunGenerator(host);

        // Then
        Assert.Contains(diagnostics, d => d.Id == "SENDR002");
    }

    [Fact]
    public void GivenTwoExternalAssembliesHandleSameSharedRequest_WhenGeneratorRuns_ThenSendr002Reported()
    {
        // Given — a "contracts" assembly declares the request/response types, and two independent
        // library assemblies (each with their own marker) declare a competing handler for it.
        const string contractsSource = """
            using Davish.Sendr;

            namespace Contracts;

            public sealed record SharedQuery : IQuery<SharedResponse>;
            public sealed record SharedResponse;
            """;
        var contracts = CreateCompilation("ContractsAssembly", contractsSource);

        const string libraryASource = """
            using Contracts;
            using Davish.Sendr;

            namespace LibA;

            public sealed class MarkerA;

            public sealed class HandlerA : IQueryHandler<SharedQuery, SharedResponse>
            {
                public Task<SharedResponse> HandleAsync(SharedQuery query, CancellationToken cancellationToken)
                    => Task.FromResult(new SharedResponse());
            }
            """;
        var libraryA = CreateCompilation("LibraryAssemblyA", libraryASource, contracts.ToMetadataReference());

        const string libraryBSource = """
            using Contracts;
            using Davish.Sendr;

            namespace LibB;

            public sealed class MarkerB;

            public sealed class HandlerB : IQueryHandler<SharedQuery, SharedResponse>
            {
                public Task<SharedResponse> HandleAsync(SharedQuery query, CancellationToken cancellationToken)
                    => Task.FromResult(new SharedResponse());
            }
            """;
        var libraryB = CreateCompilation("LibraryAssemblyB", libraryBSource, contracts.ToMetadataReference());

        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<LibA.MarkerA>().IncludeAssemblyOf<LibB.MarkerB>()));\n" +
                          HostSuffix;
        var host = CreateCompilation(
            "HostAssembly", hostSource, contracts.ToMetadataReference(), libraryA.ToMetadataReference(), libraryB.ToMetadataReference());

        // When
        var (_, diagnostics) = RunGenerator(host);

        // Then
        Assert.Contains(diagnostics, d => d.Id == "SENDR002");
    }

    [Fact]
    public void GivenUnsupportedConfigurationSyntax_WhenGeneratorRuns_ThenSendr005Reported()
    {
        // Given — a delegate variable passed to UseGenerators() instead of an inline lambda is not
        // statically resolvable.
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = """
            using System;
            using Davish.Sendr;
            using Microsoft.Extensions.DependencyInjection;

            public static class Wiring
            {
                public static void Configure(IServiceCollection services)
                {
                    Action<GeneratedSenderOptions> cfg = g => g.IncludeAssemblyOf<Lib.Marker>();
                    services.AddSendr(o => o.UseGenerators(cfg));
                }
            }
            """;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (_, diagnostics) = RunGenerator(host);

        // Then
        Assert.Contains(diagnostics, d => d.Id == "SENDR005");
    }

    [Fact]
    public void GivenInconsistentAssemblySetsAcrossCalls_WhenGeneratorRuns_ThenSendr006Reported()
    {
        // Given — two UseGenerators() call sites for the Sender in one compilation, one with an
        // inclusion and one without: ambiguous, since there's only one shared dispatch table.
        var library = CreateCompilation("LibraryAssembly", LibrarySource);
        var hostSource = """
            using Davish.Sendr;
            using Microsoft.Extensions.DependencyInjection;

            public static class Wiring
            {
                public static void ConfigureA(IServiceCollection services)
                {
                    services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));
                }

                public static void ConfigureB(IServiceCollection services)
                {
                    services.AddSendr(o => o.UseGenerators());
                }
            }
            """;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (_, diagnostics) = RunGenerator(host);

        // Then
        Assert.Contains(diagnostics, d => d.Id == "SENDR006");
    }

    [Fact]
    public void GivenInternalExternalHandlerWithoutFriendAccess_WhenGeneratorRuns_ThenSendr007Reported()
    {
        // Given
        var librarySource = """
            using Davish.Sendr;

            namespace Lib;

            public sealed class Marker;

            public sealed record InternalLibRequest : IQuery<InternalLibResponse>;
            public sealed record InternalLibResponse;

            internal sealed class InternalLibRequestHandler : IQueryHandler<InternalLibRequest, InternalLibResponse>
            {
                public Task<InternalLibResponse> HandleAsync(InternalLibRequest query, CancellationToken cancellationToken)
                    => Task.FromResult(new InternalLibResponse());
            }
            """;
        var library = CreateCompilation("LibraryAssembly", librarySource);
        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));\n" +
                          HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (_, diagnostics) = RunGenerator(host);

        // Then
        Assert.Contains(diagnostics, d => d.Id == "SENDR007");
    }

    [Fact]
    public void GivenInternalExternalHandlerWithFriendAccess_WhenGeneratorRuns_ThenDiscoveredAndCompilesClean()
    {
        // Given — same as the previous test, but the library grants HostAssembly friend access.
        var librarySource = """
            using System.Runtime.CompilerServices;
            using Davish.Sendr;

            [assembly: InternalsVisibleTo("HostAssembly")]

            namespace Lib;

            public sealed class Marker;

            public sealed record InternalLibRequest : IQuery<InternalLibResponse>;
            public sealed record InternalLibResponse;

            internal sealed class InternalLibRequestHandler : IQueryHandler<InternalLibRequest, InternalLibResponse>
            {
                public Task<InternalLibResponse> HandleAsync(InternalLibRequest query, CancellationToken cancellationToken)
                    => Task.FromResult(new InternalLibResponse());
            }
            """;
        var library = CreateCompilation("LibraryAssembly", librarySource);
        var hostSource = HostUsingsAndWiringPrefix +
                          "        services.AddSendr(o => o.UseGenerators(g => g.IncludeAssemblyOf<Lib.Marker>()));\n" +
                          HostSuffix;
        var host = CreateCompilation("HostAssembly", hostSource, library.ToMetadataReference());

        // When
        var (output, diagnostics) = RunGenerator(host);

        // Then
        Assert.Empty(diagnostics);
        Assert.Empty(CompileErrors(output));
        var generated = output.SyntaxTrees.Select(t => t.ToString()).Single(t => t.Contains("GeneratedSender"));
        Assert.Contains("Lib.InternalLibRequestHandler", generated);
    }
}
