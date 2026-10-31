using Microsoft.CodeAnalysis;

namespace Davish.Sendr;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor InvalidDecorator = new(
        id: "SENDR001",
        title: "Invalid [DecorateWith] usage",
        messageFormat: "{0}",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousHandler = new(
        id: "SENDR002",
        title: "Multiple handlers registered for the same request",
        messageFormat: "'{0}' already has a handler ('{1}'); '{2}' cannot also handle it. Remove one.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericHandlerSkipped = new(
        id: "SENDR003",
        title: "Open generic handler is not discovered",
        messageFormat: "'{0}' is a generic type and was skipped by the Davish.Sendr source generator. " +
                        "Register its closed types manually with AddRequestHandler/AddStreamRequestHandler and similar, " +
                        "without UseGenerators().",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ValueTypeHandlerNotSupported = new(
        id: "SENDR004",
        title: "Value type cannot be a Davish.Sendr handler",
        messageFormat: "'{0}' is a struct/record struct and cannot be used as a Davish.Sendr handler — " +
                        "handler registration (generated or manual, via AddRequestHandler and similar) " +
                        "requires a reference type. Change it to a class or record class.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedGeneratorConfiguration = new(
        id: "SENDR005",
        title: "Unsupported UseGenerators() configuration",
        messageFormat: "{0}",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InconsistentGeneratorConfiguration = new(
        id: "SENDR006",
        title: "Inconsistent UseGenerators() assembly configuration",
        messageFormat: "This 'UseGenerators()' call includes a different set of assemblies than another " +
                        "'UseGenerators()' call for the same {0} in this project. Every call for the same " +
                        "{0} in one compilation must declare the same 'IncludeAssemblyOf<...>()' set.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExternalHandlerNotAccessible = new(
        id: "SENDR007",
        title: "Handler or decorator is not accessible",
        messageFormat: "'{0}' was discovered in an included assembly but is not accessible from this project. " +
                        "Make it public (including any containing type), or grant this project access with " +
                        "[assembly: InternalsVisibleTo(\"...\")] in the library that declares it.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Same ID and title as ExternalHandlerNotAccessible: the cause is the same (the generated code
    // cannot reach the type), only the fix differs for a type declared in this project.
    public static readonly DiagnosticDescriptor LocalHandlerNotAccessible = new(
        id: "SENDR007",
        title: "Handler or decorator is not accessible",
        messageFormat: "'{0}' is declared in this project but is not accessible to the generated code. " +
                        "Make it internal or public, including any containing type.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SendrNotReferenced = new(
        id: "SENDR008",
        title: "Davish.Sendr is not referenced",
        messageFormat: "Davish.Sendr.Generators is referenced but Davish.Sendr is not, so nothing is generated. " +
                        "Reference Davish.Sendr, or remove the generator from this project and discover its handlers " +
                        "from the host with UseGenerators(g => g.IncludeAssemblyOf<TMarker>()).",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
