using System.Collections.Immutable;
using System.Text;
using Davish.Sendr.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Davish.Sendr;

/// <summary>
/// Discovers <c>IRequestHandler</c>/<c>IStreamRequestHandler</c>/<c>ICommandHandler</c>/
/// <c>IQueryHandler</c> implementations in the current compilation and generates
/// <c>UseGenerators()</c>: a self-contained DI registration plus a reflection-free
/// <c>ISender</c>/<c>IStreamSender</c> implementation that dispatches via compile-time-built
/// lookup tables instead of <c>MakeGenericType</c> + compiled expression trees.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SendrSourceGenerator : IIncrementalGenerator
{
    private const string RequestHandlerMetadataName = "Davish.Sendr.IRequestHandler`1";
    private const string RequestResponseHandlerMetadataName = "Davish.Sendr.IRequestHandler`2";
    private const string StreamHandlerMetadataName = "Davish.Sendr.IStreamRequestHandler`2";
    private const string CommandHandlerMetadataName = "Davish.Sendr.ICommandHandler`1";
    private const string CommandResponseHandlerMetadataName = "Davish.Sendr.ICommandHandler`2";
    private const string QueryHandlerMetadataName = "Davish.Sendr.IQueryHandler`2";
    private const string NotificationHandlerMetadataName = "Davish.Sendr.INotificationHandler`1";
    private const string DecorateAttributeNamespace = "Davish.Sendr";
    private const string DecorateAttributeName = "DecorateWithAttribute";
    private const string RequestDecoratorMetadataName = "Davish.Sendr.IRequestDecorator";
    private const string RequestDecoratorWithResponseMetadataName = "Davish.Sendr.IRequestDecorator+WithResponse";
    private const string StreamDecoratorMetadataName = "Davish.Sendr.IStreamRequestDecorator";
    private const string CommandDecoratorMetadataName = "Davish.Sendr.ICommandDecorator";
    private const string CommandDecoratorWithResponseMetadataName = "Davish.Sendr.ICommandDecorator+WithResponse";
    private const string QueryDecoratorMetadataName = "Davish.Sendr.IQueryDecorator";
    private const string NotificationDecoratorMetadataName = "Davish.Sendr.INotificationDecorator";

    // IPublisher's presence (rather than INotificationHandler`1's) is the actual signal that
    // emitting GeneratedPublisher/NotificationOptionsGeneratedExtensions is safe: the generated
    // UseGenerators() overload for NotificationOptions references NotificationOptions itself,
    // which — like SendrOptions for the request/command/query side — lives only in the Sendr
    // implementation assembly, not Sendr.Abstractions. A consumer could in principle implement
    // INotificationHandler<T> (from Abstractions) without referencing Sendr proper at all; gating
    // on IPublisher instead of the handler interface avoids emitting a NotificationOptions
    // reference into a compilation where that type doesn't exist.
    private const string PublisherMetadataName = "Davish.Sendr.IPublisher";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .CreateSyntaxProvider(
                // TypeDeclarationSyntax (not ClassDeclarationSyntax) so a record handler — a
                // distinct syntax node kind in Roslyn — is discovered instead of being invisible
                // to this predicate with no diagnostic at all (unlike an open generic handler,
                // which gets SENDR003). This also lets struct/record struct declarations reach
                // BuildModel, where they're rejected with SENDR004 (handler registration requires
                // a reference type) instead of silently producing codegen that fails to compile.
                predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, ct) => Analyze(ctx, ct))
            .SelectMany(static (r, _) => r)
            .Collect();

        var hasNotificationSupport = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(PublisherMetadataName) is not null);

        var combined = results.Combine(hasNotificationSupport);

        context.RegisterSourceOutput(combined, static (spc, combined) => Emit(spc, combined.Left, combined.Right));
    }

    private readonly record struct AnalysisResult(HandlerModel? Model, ImmutableArray<Diagnostic> Diagnostics);

    private static ImmutableArray<AnalysisResult> Analyze(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (TypeDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol)
            return ImmutableArray<AnalysisResult>.Empty;

        if (classSymbol.IsAbstract)
            return ImmutableArray<AnalysisResult>.Empty;

        var compilation = ctx.SemanticModel.Compilation;
        var requestHandler1 = compilation.GetTypeByMetadataName(RequestHandlerMetadataName);
        var requestHandler2 = compilation.GetTypeByMetadataName(RequestResponseHandlerMetadataName);
        var streamHandler2 = compilation.GetTypeByMetadataName(StreamHandlerMetadataName);
        var commandHandler1 = compilation.GetTypeByMetadataName(CommandHandlerMetadataName);
        var commandHandler2 = compilation.GetTypeByMetadataName(CommandResponseHandlerMetadataName);
        var queryHandler2 = compilation.GetTypeByMetadataName(QueryHandlerMetadataName);
        var notificationHandler1 = compilation.GetTypeByMetadataName(NotificationHandlerMetadataName);

        if (requestHandler1 is null && requestHandler2 is null && streamHandler2 is null &&
            commandHandler1 is null && commandHandler2 is null && queryHandler2 is null &&
            notificationHandler1 is null)
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
            else if (commandHandler2 is not null && SymbolEqualityComparer.Default.Equals(original, commandHandler2))
                kind = HandlerKind.CommandResponse;
            else if (commandHandler1 is not null && SymbolEqualityComparer.Default.Equals(original, commandHandler1))
                kind = HandlerKind.Command;
            else if (queryHandler2 is not null && SymbolEqualityComparer.Default.Equals(original, queryHandler2))
                kind = HandlerKind.Query;
            else if (notificationHandler1 is not null && SymbolEqualityComparer.Default.Equals(original, notificationHandler1))
                kind = HandlerKind.Notification;
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
        if (classSymbol.IsValueType)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ValueTypeHandlerNotSupported,
                classSymbol.Locations.FirstOrDefault(),
                classSymbol.ToDisplayString());
            return new AnalysisResult(null, [diagnostic]);
        }

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
        var responseType = kind is HandlerKind.Request or HandlerKind.Command or HandlerKind.Notification
            ? null
            : ToGlobalName(typeArgs[1]);

        var (requiredMetadataName, requiredDisplayName) = kind switch
        {
            HandlerKind.Request => (RequestDecoratorMetadataName, "Davish.Sendr.IRequestDecorator"),
            HandlerKind.RequestResponse => (RequestDecoratorWithResponseMetadataName, "Davish.Sendr.IRequestDecorator.WithResponse"),
            HandlerKind.Stream => (StreamDecoratorMetadataName, "Davish.Sendr.IStreamRequestDecorator"),
            HandlerKind.Command => (CommandDecoratorMetadataName, "Davish.Sendr.ICommandDecorator"),
            HandlerKind.CommandResponse => (CommandDecoratorWithResponseMetadataName, "Davish.Sendr.ICommandDecorator.WithResponse"),
            HandlerKind.Query => (QueryDecoratorMetadataName, "Davish.Sendr.IQueryDecorator"),
            HandlerKind.Notification => (NotificationDecoratorMetadataName, "Davish.Sendr.INotificationDecorator"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var requiredInterfaceSymbol = compilation.GetTypeByMetadataName(requiredMetadataName);

        var (decorators, diagnostics) = ExtractDecorators(classSymbol, requiredInterfaceSymbol, requiredDisplayName);

        var model = new HandlerModel(kind, requestType, responseType, ToGlobalName(classSymbol), decorators);
        return new AnalysisResult(model, diagnostics);
    }

    /// <summary>
    /// A <c>[DecorateWith&lt;T1, ..., Tn&gt;]</c> attribute is one of the eight generic
    /// <c>Davish.Sendr.DecorateWithAttribute</c> arities — matched by name/namespace/arity rather
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
                    $"'{classSymbol.ToDisplayString()}' has more than one [DecorateWith<...>]. " +
                    "Combine them into a single attribute, e.g. [DecorateWith<TransactionDecorator, LoggingDecorator>]."));
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

    private static void Emit(SourceProductionContext spc, ImmutableArray<AnalysisResult> results, bool hasNotificationSupport)
    {
        foreach (var result in results)
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

        // Every other handler kind allows exactly one handler per request type — a second one is
        // ambiguous (SENDR002). Notification handlers are the opposite: any number of classes may
        // handle the same notification type, so they skip this dedup entirely and are all kept.
        var seen = new System.Collections.Generic.Dictionary<(HandlerKind, string, string?), HandlerModel>();
        var deduped = ImmutableArray.CreateBuilder<HandlerModel>();

        foreach (var result in results)
        {
            if (result.Model is not { } model)
                continue;

            if (model.Kind == HandlerKind.Notification)
            {
                deduped.Add(model);
                continue;
            }

            // ResponseType is part of the key because IRequest<out TResponse>/ICommand<out
            // TResponse>/IQuery<out TResponse>/IStreamRequest<out TResponse> are all covariant in
            // TResponse, so a single request/command/query type can legally implement e.g. both
            // IQuery<int> and IQuery<string>. Keying on RequestType alone (mirroring the same gap
            // HandlerRegistry's runtime cache had) would misreport that as an ambiguous handler.
            var key = (model.Kind, model.RequestType, model.ResponseType);
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

        // Without Sendr referenced (IPublisher unresolvable), there is nothing valid to emit for
        // any discovered notification handler — GeneratedPublisher itself couldn't compile — so
        // they're dropped rather than passed to SourceBuilder.
        var models = hasNotificationSupport
            ? deduped.ToImmutable()
            : deduped.Where(m => m.Kind != HandlerKind.Notification).ToImmutableArray();

        var source = SourceBuilder.Build(models, hasNotificationSupport);
        spc.AddSource("Sendr.Generated.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat GlobalFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static string ToGlobalName(ISymbol symbol) => symbol.ToDisplayString(GlobalFormat);
}
