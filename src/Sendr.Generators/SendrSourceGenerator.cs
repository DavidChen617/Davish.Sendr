using System.Collections.Immutable;
using System.Text;
using Davish.Sendr.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Davish.Sendr;

/// <summary>
/// Discovers <c>IRequestHandler</c>/<c>IStreamRequestHandler</c>/<c>ICommandHandler</c>/
/// <c>IQueryHandler</c> implementations in the current compilation — and, when declared via
/// <c>UseGenerators(g =&gt; g.IncludeAssemblyOf&lt;TMarker&gt;())</c>, in explicitly included
/// referenced assemblies too — and generates <c>UseGenerators()</c>: a self-contained DI
/// registration plus a reflection-free <c>ISender</c>/<c>IStreamSender</c> implementation that
/// dispatches via compile-time-built lookup tables instead of <c>MakeGenericType</c> + compiled
/// expression trees.
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
    private const string SendrOptionsMetadataName = "Davish.Sendr.SendrOptions";
    private const string NotificationOptionsMetadataName = "Davish.Sendr.NotificationOptions";
    private const string IncludeAssemblyOfMethodName = "IncludeAssemblyOf";
    private const string RunAsMethodName = "RunAs";
    private const string UseGeneratorsMethodName = "UseGenerators";

    // IPublisher's presence (rather than INotificationHandler`1's) is the actual signal that
    // emitting GeneratedPublisher/NotificationOptionsGeneratedExtensions is safe: the generated
    // UseGenerators() overload for NotificationOptions references NotificationOptions itself,
    // which — like SendrOptions for the request/command/query side — lives only in the Sendr
    // implementation assembly, not Sendr.Abstractions. A consumer could in principle implement
    // INotificationHandler<T> (from Abstractions) without referencing Sendr proper at all; gating
    // on IPublisher instead of the handler interface avoids emitting a NotificationOptions
    // reference into a compilation where that type doesn't exist.
    private const string PublisherMetadataName = "Davish.Sendr.IPublisher";

    private enum ConfigKind
    {
        Sender,
        Notification,
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var localResults = context.SyntaxProvider
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

        var configCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsUseGeneratorsCandidate(node),
                transform: static (ctx, ct) => AnalyzeConfig(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        var hasNotificationSupport = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(PublisherMetadataName) is not null);

        var combined = localResults
            .Combine(configCandidates)
            .Combine(hasNotificationSupport)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var (((local, configs), hasNotification), compilation) = data;
            Emit(spc, local, configs, hasNotification, compilation);
        });
    }

    private readonly record struct AnalysisResult(HandlerModel? Model, ImmutableArray<Diagnostic> Diagnostics);

    // A single `IncludeAssemblyOf<TMarker>()` call found inside a `UseGenerators(g => ...)`
    // configuration lambda. `Location` is the call's own site, used as the diagnostic/dispatch
    // location fallback for anything discovered in `MarkerType`'s assembly, since a metadata
    // symbol from that assembly has no location in *this* compilation.
    private sealed record MarkerInclusion(ITypeSymbol MarkerType, Location Location);

    // One `UseGenerators(...)` call site recognized as configuring either the Sender or the
    // Publisher side. `Diagnostics` holds syntax-level problems found while parsing the
    // configuration lambda itself (unsupported constructs) — reported unconditionally.
    private sealed record UseGeneratorsConfigCandidate(
        ConfigKind Kind,
        Location InvocationLocation,
        ImmutableArray<MarkerInclusion> Inclusions,
        ImmutableArray<Diagnostic> Diagnostics);

    private static ImmutableArray<AnalysisResult> Analyze(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (TypeDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol)
            return ImmutableArray<AnalysisResult>.Empty;

        return AnalyzeClassSymbol(classSymbol, ctx.SemanticModel.Compilation, fallbackLocation: null);
    }

    /// <summary>
    /// Classifies every handler interface <paramref name="classSymbol"/> implements into a
    /// <see cref="HandlerModel"/>. Symbol-based rather than syntax-based, so it works identically
    /// whether <paramref name="classSymbol"/> came from this compilation's own syntax trees or
    /// from walking an externally included assembly's metadata — cross-assembly interface
    /// identity (e.g. <c>IRequestHandler&lt;T&gt;</c> declared in Sendr.Abstractions) is stable
    /// either way because both views are resolved against the same host <paramref name="compilation"/>.
    /// </summary>
    /// <param name="classSymbol">The candidate handler type to classify.</param>
    /// <param name="compilation">The host compilation to resolve handler/decorator interfaces against.</param>
    /// <param name="fallbackLocation">
    /// Used for diagnostics when <paramref name="classSymbol"/> has no location in this
    /// compilation's own source (i.e. it came from an included external assembly) — the location
    /// of the <c>IncludeAssemblyOf&lt;TMarker&gt;()</c> call that pulled its assembly in.
    /// </param>
    private static ImmutableArray<AnalysisResult> AnalyzeClassSymbol(
        INamedTypeSymbol classSymbol, Compilation compilation, Location? fallbackLocation)
    {
        if (classSymbol.IsAbstract)
            return ImmutableArray<AnalysisResult>.Empty;

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

            results.Add(BuildModel(classSymbol, iface, kind, compilation, fallbackLocation));
        }

        return results.ToImmutable();
    }

    /// <summary>
    /// A source location for <paramref name="symbol"/> that <see cref="SourceProductionContext.ReportDiagnostic"/>
    /// will actually accept for <paramref name="compilation"/> — falling back to
    /// <paramref name="fallbackLocation"/> (typically the host's <c>IncludeAssemblyOf&lt;TMarker&gt;()</c>
    /// call site) when it doesn't have one.
    /// </summary>
    /// <remarks>
    /// Checking <see cref="Location.IsInSource"/> alone isn't enough: a symbol reached through a
    /// live <see cref="CompilationReference"/> (as opposed to a compiled, on-disk assembly
    /// reference — the case a real multi-project MSBuild/dotnet build always produces, but which a
    /// generator-testing harness commonly doesn't) reports source locations from its *own*,
    /// separate <see cref="Compilation"/>. Reporting a diagnostic at one of those throws
    /// <see cref="ArgumentException"/>, since it isn't part of <paramref name="compilation"/> —
    /// so this also confirms the location's syntax tree actually belongs here.
    /// </remarks>
    private static Location LocationInCompilation(ISymbol symbol, Compilation compilation, Location? fallbackLocation) =>
        symbol.Locations.FirstOrDefault(l => l.IsInSource && compilation.ContainsSyntaxTree(l.SourceTree)) ??
        fallbackLocation ?? Location.None;

    private static AnalysisResult BuildModel(
        INamedTypeSymbol classSymbol,
        INamedTypeSymbol interfaceSymbol,
        HandlerKind kind,
        Compilation compilation,
        Location? fallbackLocation)
    {
        var location = LocationInCompilation(classSymbol, compilation, fallbackLocation);

        // Roslyn accessibility, not "is this the current compilation": a handler declared in an
        // externally included assembly is fine as long as it's public (or internal with a legal
        // InternalsVisibleTo granted to this compilation) — the same rule a local handler already
        // satisfies trivially, since anything in the current compilation's own assembly is always
        // accessible to code generated into that same assembly.
        if (!compilation.IsSymbolAccessibleWithin(classSymbol, compilation.Assembly))
        {
            var inaccessible = Diagnostic.Create(
                DiagnosticDescriptors.ExternalHandlerNotAccessible, location, classSymbol.ToDisplayString());
            return new AnalysisResult(null, [inaccessible]);
        }

        if (classSymbol.IsValueType)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ValueTypeHandlerNotSupported, location, classSymbol.ToDisplayString());
            return new AnalysisResult(null, [diagnostic]);
        }

        if (classSymbol.TypeParameters.Length > 0)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.OpenGenericHandlerSkipped, location, classSymbol.ToDisplayString());
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

        var (decorators, diagnostics) = ExtractDecorators(
            classSymbol, requiredInterfaceSymbol, requiredDisplayName, compilation, fallbackLocation);

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
        string requiredDisplayName,
        Compilation compilation,
        Location? fallbackLocation)
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
                    : LocationInCompilation(classSymbol, compilation, fallbackLocation);
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
            : LocationInCompilation(classSymbol, compilation, fallbackLocation);

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

            if (!compilation.IsSymbolAccessibleWithin(decoratorType, compilation.Assembly))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.ExternalHandlerNotAccessible, location2, decoratorType.ToDisplayString()));
                continue;
            }

            decorators.Add(new DecoratorRef(ToGlobalName(decoratorType)));
        }

        return (decorators.ToImmutable(), diagnostics.ToImmutable());
    }

    // ---- UseGenerators(g => g.IncludeAssemblyOf<TMarker>()) configuration discovery ----
    //
    // The generated UseGenerators() overload that accepts this lambda doesn't exist yet in the
    // compilation this generator run is analyzing (we're the ones about to emit it), so this
    // can't rely on semantic binding of the UseGenerators(...) invocation itself. Instead: find
    // every invocation syntactically named "UseGenerators", confirm — via the semantic model —
    // that its receiver's type actually is SendrOptions/NotificationOptions (both hand-written
    // types that already exist in Sendr.dll, so this resolves regardless of whether our generated
    // overload exists), then walk the argument lambda's body purely syntactically for direct
    // IncludeAssemblyOf<T>()/RunAs(...) calls chained off the lambda's own parameter. Resolving
    // just the generic type argument `T` (not the enclosing IncludeAssemblyOf<T>() invocation
    // itself) works even while the surrounding call doesn't bind, since `T` is an ordinary type
    // reference independent of the invocation's overload resolution.

    private static bool IsUseGeneratorsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: UseGeneratorsMethodName } };

    private static UseGeneratorsConfigCandidate? AnalyzeConfig(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var semanticModel = ctx.SemanticModel;
        var compilation = semanticModel.Compilation;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, ct).Type;
        if (receiverType is null)
            return null;

        var sendrOptionsType = compilation.GetTypeByMetadataName(SendrOptionsMetadataName);
        var notificationOptionsType = compilation.GetTypeByMetadataName(NotificationOptionsMetadataName);

        ConfigKind kind;
        if (sendrOptionsType is not null && SymbolEqualityComparer.Default.Equals(receiverType, sendrOptionsType))
            kind = ConfigKind.Sender;
        else if (notificationOptionsType is not null && SymbolEqualityComparer.Default.Equals(receiverType, notificationOptionsType))
            kind = ConfigKind.Notification;
        else
            return null; // Some other, unrelated UseGenerators(...) method — not ours.

        var invocationLocation = invocation.GetLocation();
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count == 0)
            return new UseGeneratorsConfigCandidate(kind, invocationLocation, ImmutableArray<MarkerInclusion>.Empty, ImmutableArray<Diagnostic>.Empty);

        if (arguments.Count != 1)
            return null; // Not a shape UseGenerators ever has; leave it alone.

        string paramName;
        SyntaxNode body;
        switch (arguments[0].Expression)
        {
            case SimpleLambdaExpressionSyntax simple:
                paramName = simple.Parameter.Identifier.Text;
                body = simple.Body;
                break;

            case ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } paren:
                paramName = paren.ParameterList.Parameters[0].Identifier.Text;
                body = paren.Body;
                break;

            case ParenthesizedLambdaExpressionSyntax badParen:
                return Unsupported(kind, invocationLocation, badParen.GetLocation(),
                    "The UseGenerators() configuration lambda must take exactly one parameter.");

            default:
                return Unsupported(kind, invocationLocation, arguments[0].GetLocation(),
                    "UseGenerators() only recognizes a single inline lambda argument, e.g. " +
                    "'g => g.IncludeAssemblyOf<TMarker>()' — a delegate variable, method group, or a " +
                    "helper/extension method wrapping the configuration is not statically resolvable.");
        }

        ImmutableArray<SyntaxNode> statements;
        if (body is BlockSyntax block)
        {
            var nonExpression = block.Statements.FirstOrDefault(s => s is not ExpressionStatementSyntax);
            if (nonExpression is not null)
                return Unsupported(kind, invocationLocation, nonExpression.GetLocation(),
                    "Only direct 'g.IncludeAssemblyOf<TMarker>()' / 'x.RunAs(...)' calls are supported inside " +
                    "UseGenerators() — loops, conditionals, and other statements are not statically resolvable.");

            statements = block.Statements.Cast<ExpressionStatementSyntax>().Select(s => (SyntaxNode)s.Expression).ToImmutableArray();
        }
        else
        {
            statements = [body];
        }

        var inclusions = ImmutableArray.CreateBuilder<MarkerInclusion>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var statement in statements)
            WalkConfigChain(statement, paramName, semanticModel, ct, inclusions, diagnostics);

        return new UseGeneratorsConfigCandidate(kind, invocationLocation, inclusions.ToImmutable(), diagnostics.ToImmutable());
    }

    private static UseGeneratorsConfigCandidate Unsupported(ConfigKind kind, Location invocationLocation, Location badLocation, string message) =>
        new(kind, invocationLocation, ImmutableArray<MarkerInclusion>.Empty,
            [Diagnostic.Create(DiagnosticDescriptors.UnsupportedGeneratorConfiguration, badLocation, message)]);

    /// <summary>
    /// Walks a fluent chain such as <c>g.IncludeAssemblyOf&lt;A&gt;().IncludeAssemblyOf&lt;B&gt;()</c>
    /// rooted at the configuration lambda's own parameter (<paramref name="paramName"/>), collecting
    /// every <c>IncludeAssemblyOf&lt;T&gt;()</c> call. <c>RunAs(...)</c> calls are recognized and
    /// skipped (they only affect the generated Publisher's runtime behavior, not what's discovered).
    /// Anything else — a call not rooted at the parameter, or a call that isn't one of these two
    /// methods — is reported rather than silently ignored, since silently dropping it could produce
    /// a dispatch table the author believed was complete.
    /// </summary>
    private static void WalkConfigChain(
        SyntaxNode expression,
        string paramName,
        SemanticModel semanticModel,
        System.Threading.CancellationToken ct,
        ImmutableArray<MarkerInclusion>.Builder inclusions,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var calls = new List<InvocationExpressionSyntax>();
        SyntaxNode current = expression;

        while (current is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax)
        {
            calls.Add(invocation);
            current = ((MemberAccessExpressionSyntax)invocation.Expression).Expression;
        }

        if (current is not IdentifierNameSyntax identifier || identifier.Identifier.Text != paramName)
        {
            diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedGeneratorConfiguration, expression.GetLocation(),
                "Only direct calls on the configuration parameter are supported (e.g. 'g.IncludeAssemblyOf<TMarker>()'); " +
                "this expression isn't a statically resolvable call chain."));
            return;
        }

        calls.Reverse();

        foreach (var call in calls)
        {
            var memberAccess = (MemberAccessExpressionSyntax)call.Expression;
            var methodName = memberAccess.Name.Identifier.Text;

            if (methodName == RunAsMethodName)
                continue;

            if (methodName != IncludeAssemblyOfMethodName ||
                memberAccess.Name is not GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic)
            {
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedGeneratorConfiguration, call.GetLocation(),
                    $"'{methodName}' is not a recognized UseGenerators() configuration call."));
                continue;
            }

            var typeArgSyntax = generic.TypeArgumentList.Arguments[0];
            var markerType = semanticModel.GetTypeInfo(typeArgSyntax, ct).Type;

            if (markerType is null or IErrorTypeSymbol)
            {
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedGeneratorConfiguration, typeArgSyntax.GetLocation(),
                    "Could not resolve the marker type passed to IncludeAssemblyOf<TMarker>()."));
                continue;
            }

            inclusions.Add(new MarkerInclusion(markerType, call.GetLocation()));
        }
    }

    // ---- External assembly walking ----

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }

        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in GetAllNamedTypes(child))
                yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in GetNestedTypes(nested))
                yield return deeper;
        }
    }

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<AnalysisResult> localResults,
        ImmutableArray<UseGeneratorsConfigCandidate> configs,
        bool hasNotificationSupport,
        Compilation compilation)
    {
        foreach (var config in configs)
            foreach (var diagnostic in config.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

        var (senderAssemblies, senderInconsistent) = ResolveIncludedAssemblies(
            configs.Where(c => c.Kind == ConfigKind.Sender), compilation, out var hostLocationByAssembly);
        var (notificationAssemblies, notificationInconsistent) = ResolveIncludedAssemblies(
            configs.Where(c => c.Kind == ConfigKind.Notification), compilation, out var notificationHostLocations);

        foreach (var kvp in notificationHostLocations)
            if (!hostLocationByAssembly.ContainsKey(kvp.Key))
                hostLocationByAssembly[kvp.Key] = kvp.Value; // First occurrence (sender) wins if both include it.

        if (senderInconsistent is { } senderConflict)
            foreach (var location in senderConflict)
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InconsistentGeneratorConfiguration, location, "Sender"));

        if (notificationInconsistent is { } notificationConflict)
            foreach (var location in notificationConflict)
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.InconsistentGeneratorConfiguration, location, "Publisher"));

        var assembliesToWalk = new List<IAssemblySymbol>();
        foreach (var assembly in senderAssemblies.Concat(notificationAssemblies))
            if (!assembliesToWalk.Contains(assembly, SymbolEqualityComparer.Default))
                assembliesToWalk.Add(assembly);

        var externalResultsByAssembly = new Dictionary<IAssemblySymbol, ImmutableArray<AnalysisResult>>(SymbolEqualityComparer.Default);
        foreach (var assembly in assembliesToWalk)
        {
            hostLocationByAssembly.TryGetValue(assembly, out var hostLocation);
            var builder = ImmutableArray.CreateBuilder<AnalysisResult>();
            foreach (var classSymbol in GetAllNamedTypes(assembly.GlobalNamespace))
                builder.AddRange(AnalyzeClassSymbol(classSymbol, compilation, hostLocation));
            externalResultsByAssembly[assembly] = builder.ToImmutable();
        }

        foreach (var results in externalResultsByAssembly.Values)
            foreach (var result in results)
                foreach (var diagnostic in result.Diagnostics)
                    spc.ReportDiagnostic(diagnostic);

        // Notification handlers never came from local syntax discovery's non-Notification results
        // and vice versa, so this reuses the exact same dedup pipeline the local-only generator
        // always had — local results first, then each included assembly's — for a deterministic,
        // stable ordering of which handler "already has" a request type when SENDR002 fires.
        var allResults = localResults
            .Concat(senderAssemblies.SelectMany(a => externalResultsByAssembly[a])
                .Where(r => r.Model is not { Kind: HandlerKind.Notification }))
            .Concat(notificationAssemblies.SelectMany(a => externalResultsByAssembly[a])
                .Where(r => r.Model is { Kind: HandlerKind.Notification }))
            .ToImmutableArray();

        // Every other handler kind allows exactly one handler per request type — a second one is
        // ambiguous (SENDR002). Notification handlers are the opposite: any number of classes may
        // handle the same notification type, so they skip this dedup entirely and are all kept.
        var seen = new Dictionary<(HandlerKind, string, string?), HandlerModel>();
        var deduped = ImmutableArray.CreateBuilder<HandlerModel>();

        foreach (var result in allResults)
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
                if (existing == model)
                    continue; // The exact same handler (identical decorators too) reached via more than one inclusion path — not a conflict.

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

    /// <summary>
    /// Normalizes every <paramref name="configs"/> call's inclusion set into a deduplicated list of
    /// <see cref="IAssemblySymbol"/> (excluding the current compilation's own assembly, which is
    /// always covered by local syntax discovery already), and checks that every call for the same
    /// kind agrees on the same set. Also fills <paramref name="hostLocationByAssembly"/> with the
    /// first inclusion call site that named each assembly, used as a diagnostic/fallback location
    /// for anything discovered in it.
    /// </summary>
    private static (IReadOnlyList<IAssemblySymbol> Assemblies, IReadOnlyList<Location>? ConflictLocations) ResolveIncludedAssemblies(
        IEnumerable<UseGeneratorsConfigCandidate> configs,
        Compilation compilation,
        out Dictionary<IAssemblySymbol, Location> hostLocationByAssembly)
    {
        hostLocationByAssembly = new Dictionary<IAssemblySymbol, Location>(SymbolEqualityComparer.Default);

        var perConfigSets = new List<(Location InvocationLocation, HashSet<IAssemblySymbol> Assemblies)>();

        foreach (var config in configs)
        {
            var set = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

            foreach (var inclusion in config.Inclusions)
            {
                var assembly = inclusion.MarkerType.ContainingAssembly;
                if (assembly is null || SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
                    continue; // Already covered by local discovery, or not a type with a resolvable assembly.

                set.Add(assembly);
                if (!hostLocationByAssembly.ContainsKey(assembly))
                    hostLocationByAssembly[assembly] = inclusion.Location;
            }

            perConfigSets.Add((config.InvocationLocation, set));
        }

        if (perConfigSets.Count == 0)
            return (Array.Empty<IAssemblySymbol>(), null);

        var first = perConfigSets[0].Assemblies;
        var conflicts = perConfigSets.Skip(1).Where(s => !s.Assemblies.SetEquals(first)).ToImmutableArray();

        if (conflicts.Length > 0)
        {
            var locations = new List<Location> { perConfigSets[0].InvocationLocation };
            locations.AddRange(conflicts.Select(c => c.InvocationLocation));
            return (first.ToImmutableArray(), locations);
        }

        return (first.ToImmutableArray(), null);
    }

    private static readonly SymbolDisplayFormat GlobalFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static string ToGlobalName(ISymbol symbol) => symbol.ToDisplayString(GlobalFormat);
}
