using System.Collections.Immutable;
using System.Text;
using Davish.Sendr.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Davish.Sendr;

/// <summary>
/// Discovers <c>IRequestHandler</c>/<c>IStreamRequestHandler</c> implementations in the current
/// compilation and emits <c>AddSendrGenerated()</c>: a self-contained DI registration plus a
/// reflection-free <c>ISender</c>/<c>IStreamSender</c> implementation that dispatches via a
/// compile-time type switch instead of <c>MakeGenericType</c> + compiled expression trees.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SendrSourceGenerator : IIncrementalGenerator
{
    private const string RequestHandlerMetadataName = "Davish.Sendr.IRequestHandler`1";
    private const string RequestResponseHandlerMetadataName = "Davish.Sendr.IRequestHandler`2";
    private const string StreamHandlerMetadataName = "Davish.Sendr.IStreamRequestHandler`2";
    private const string DecorateAttributeNamespace = "Davish.Sendr";
    private const string DecorateAttributeName = "DecorateAttribute";
    private const string RequestDecoratorMetadataName = "Davish.Sendr.IRequestDecorator";
    private const string RequestDecoratorWithResponseMetadataName = "Davish.Sendr.IRequestDecorator+WithResponse";
    private const string StreamDecoratorMetadataName = "Davish.Sendr.IStreamRequestDecorator";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, ct) => Analyze(ctx, ct))
            .SelectMany(static (r, _) => r)
            .Collect();

        context.RegisterSourceOutput(results, static (spc, results) => Emit(spc, results));
    }

    private readonly record struct AnalysisResult(HandlerModel? Model, ImmutableArray<Diagnostic> Diagnostics);

    private static ImmutableArray<AnalysisResult> Analyze(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol)
            return ImmutableArray<AnalysisResult>.Empty;

        if (classSymbol.IsAbstract)
            return ImmutableArray<AnalysisResult>.Empty;

        var compilation = ctx.SemanticModel.Compilation;
        var requestHandler1 = compilation.GetTypeByMetadataName(RequestHandlerMetadataName);
        var requestHandler2 = compilation.GetTypeByMetadataName(RequestResponseHandlerMetadataName);
        var streamHandler2 = compilation.GetTypeByMetadataName(StreamHandlerMetadataName);

        if (requestHandler1 is null && requestHandler2 is null && streamHandler2 is null)
            return ImmutableArray<AnalysisResult>.Empty;

        var results = ImmutableArray.CreateBuilder<AnalysisResult>();

        foreach (var iface in classSymbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            HandlerKind kind;
            if (requestHandler2 is not null && SymbolEqualityComparer.Default.Equals(original, requestHandler2))
                kind = HandlerKind.RequestResponse;
            else if (streamHandler2 is not null && SymbolEqualityComparer.Default.Equals(original, streamHandler2))
                kind = HandlerKind.Stream;
            else if (requestHandler1 is not null && SymbolEqualityComparer.Default.Equals(original, requestHandler1))
                kind = HandlerKind.Request;
            else
                continue;

            results.Add(BuildModel(classSymbol, iface, kind, compilation));
        }

        return results.ToImmutable();
    }

    private static AnalysisResult BuildModel(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol interfaceSymbol,
        HandlerKind kind,
        Compilation compilation)
    {
        if (classSymbol.TypeParameters.Length > 0)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.OpenGenericHandlerSkipped,
                classSymbol.Locations.FirstOrDefault(),
                classSymbol.ToDisplayString());
            return new AnalysisResult(null, [diagnostic]);
        }

        var typeArgs = interfaceSymbol.TypeArguments;
        var requestType = ToGlobalName(typeArgs[0]);
        var responseType = kind == HandlerKind.Request ? null : ToGlobalName(typeArgs[1]);

        var (requiredMetadataName, requiredDisplayName) = kind switch
        {
            HandlerKind.Request => (RequestDecoratorMetadataName, "Davish.Sendr.IRequestDecorator"),
            HandlerKind.RequestResponse => (RequestDecoratorWithResponseMetadataName, "Davish.Sendr.IRequestDecorator.WithResponse"),
            HandlerKind.Stream => (StreamDecoratorMetadataName, "Davish.Sendr.IStreamRequestDecorator"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var requiredInterfaceSymbol = compilation.GetTypeByMetadataName(requiredMetadataName);

        var (decorators, diagnostics) = ExtractDecorators(classSymbol, requiredInterfaceSymbol, requiredDisplayName);

        var model = new HandlerModel(kind, requestType, responseType, ToGlobalName(classSymbol), decorators);
        return new AnalysisResult(model, diagnostics);
    }

    /// <summary>
    /// A <c>[Decorate&lt;T1, ..., Tn&gt;]</c> attribute is one of the eight generic
    /// <c>Davish.Sendr.DecorateAttribute</c> arities — matched by name/namespace/arity rather
    /// than by resolving all eight metadata names up front.
    /// </summary>
    private static bool IsDecorateAttribute(INamedTypeSymbol attributeClass)
    {
        var original = attributeClass.OriginalDefinition;
        return original.Arity is >= 1 and <= 8 &&
               original.Name == DecorateAttributeName &&
               original.ContainingNamespace?.ToDisplayString() == DecorateAttributeNamespace;
    }

    private static (ImmutableArray<DecoratorRef> Decorators, ImmutableArray<Diagnostic> Diagnostics) ExtractDecorators(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol? requiredDecoratorInterface,
        string requiredDisplayName)
    {
        var matches = classSymbol.GetAttributes()
            .Where(a => a.AttributeClass is not null && IsDecorateAttribute(a.AttributeClass))
            .ToImmutableArray();

        if (matches.Length == 0)
            return (ImmutableArray<DecoratorRef>.Empty, ImmutableArray<Diagnostic>.Empty);

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (matches.Length > 1)
        {
            foreach (var extra in matches)
            {
                var location = extra.ApplicationSyntaxReference is { } syntaxRef
                    ? syntaxRef.GetSyntax().GetLocation()
                    : classSymbol.Locations.FirstOrDefault();
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.InvalidDecorator, location,
                    $"'{classSymbol.ToDisplayString()}' has more than one [Decorate<...>]. " +
                    "Combine them into a single attribute, e.g. [Decorate<TransactionDecorator, LoggingDecorator>]."));
            }

            return (ImmutableArray<DecoratorRef>.Empty, diagnostics.ToImmutable());
        }

        var attribute = matches[0];
        var attributeClass = attribute.AttributeClass!;
        var location2 = attribute.ApplicationSyntaxReference is { } syntaxRef2
            ? syntaxRef2.GetSyntax().GetLocation()
            : classSymbol.Locations.FirstOrDefault();

        var decorators = ImmutableArray.CreateBuilder<DecoratorRef>();

        foreach (var typeArg in attributeClass.TypeArguments)
        {
            if (typeArg is not INamedTypeSymbol decoratorType)
                continue;

            if (requiredDecoratorInterface is not null &&
                !decoratorType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, requiredDecoratorInterface)))
            {
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.InvalidDecorator, location2,
                    $"'{decoratorType.ToDisplayString()}' does not implement {requiredDisplayName}, " +
                    $"required to decorate '{classSymbol.ToDisplayString()}'."));
                continue;
            }

            decorators.Add(new DecoratorRef(ToGlobalName(decoratorType)));
        }

        return (decorators.ToImmutable(), diagnostics.ToImmutable());
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<AnalysisResult> results)
    {
        foreach (var result in results)
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

        var seen = new System.Collections.Generic.Dictionary<(HandlerKind, string), HandlerModel>();
        var deduped = ImmutableArray.CreateBuilder<HandlerModel>();

        foreach (var result in results)
        {
            if (result.Model is not { } model)
                continue;

            var key = (model.Kind, model.RequestType);
            if (seen.TryGetValue(key, out var existing))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.AmbiguousHandler,
                    Location.None,
                    model.RequestType, existing.HandlerType, model.HandlerType));
                continue;
            }

            seen[key] = model;
            deduped.Add(model);
        }

        var source = SourceBuilder.Build(deduped.ToImmutable());
        spc.AddSource("Sendr.Generated.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat GlobalFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static string ToGlobalName(ISymbol symbol) => symbol.ToDisplayString(GlobalFormat);
}
