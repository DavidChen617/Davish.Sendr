using System.Collections.Immutable;
using Davish.Sendr;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sendr.Generators.Tests;

/// <summary>
/// Runs <see cref="SendrSourceGenerator"/> against in-memory compilations, for scenarios that cannot exist in this
/// project itself: code that must fail to compile, or a second assembly with a particular shape.
/// </summary>
internal static class GeneratorHarness
{
    // Everything the running test host can load, minus this test assembly: it already carries this project's own
    // generated UseGenerators() extensions, which would be ambiguous next to the ones a harness run generates.
    private static readonly Lazy<ImmutableArray<MetadataReference>> BaseReferences = new(() =>
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var ownAssembly = typeof(GeneratorHarness).Assembly.Location;
        return
        [
            .. trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(path => File.Exists(path) && path != ownAssembly)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    });

    // What <ImplicitUsings>enable</ImplicitUsings> gives a real project; the generated code relies on it.
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    public static CSharpCompilation Compile(string assemblyName, string source, params MetadataReference[] references) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(GlobalUsings), CSharpSyntaxTree.ParseText(source)],
            BaseReferences.Value.AddRange(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>Emits <paramref name="library"/> and references it as metadata, the way a built ProjectReference is seen.</summary>
    public static MetadataReference AsMetadata(CSharpCompilation library)
    {
        using var image = new MemoryStream();
        var result = library.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(
                $"The {library.AssemblyName} fixture does not compile: " +
                string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    public static GeneratorRun Run(CSharpCompilation host)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SendrSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(host, out var output, out var diagnostics);
        var generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()));
        return new GeneratorRun(diagnostics, output, generated);
    }
}

/// <summary>What one generator run reported and produced.</summary>
internal sealed record GeneratorRun(ImmutableArray<Diagnostic> Diagnostics, Compilation Output, string GeneratedSource)
{
    public Diagnostic[] Sendr(string id) => [.. Diagnostics.Where(d => d.Id == id)];

    // CS1701 / CS1702 (assembly reference version unification) are off by default in SDK projects through
    // <NoWarn>; this harness builds compilations directly, so it leaves them out the same way.
    private static readonly string[] SdkSuppressedWarnings = ["CS1701", "CS1702"];

    /// <summary>Errors and warnings from compiling the host together with the generated code.</summary>
    public Diagnostic[] CompileProblems =>
        [.. Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning && !SdkSuppressedWarnings.Contains(d.Id))];

    public Diagnostic[] CompileErrors =>
        [.. Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)];

    /// <summary>Whether the generated dispatch mentions <paramref name="typeName"/>, i.e. discovery picked it up.</summary>
    public bool Dispatches(string typeName) => GeneratedSource.Contains(typeName, StringComparison.Ordinal);
}
