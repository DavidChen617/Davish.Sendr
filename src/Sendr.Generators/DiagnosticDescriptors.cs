using Microsoft.CodeAnalysis;

namespace Davish.Sendr;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor InvalidDecorator = new(
        id: "SENDR001",
        title: "Invalid [Decorate] usage",
        messageFormat: "{0}",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousHandler = new(
        id: "SENDR002",
        title: "Multiple handlers registered for the same request",
        messageFormat: "'{0}' already has a handler ('{1}'); '{2}' cannot also handle it. " +
                        "Remove one, or register manually with AddRequestHandler instead of relying on AddSendrGenerated.",
        category: "Davish.Sendr.Generators",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericHandlerSkipped = new(
        id: "SENDR003",
        title: "Open generic handler is not discovered",
        messageFormat: "'{0}' is a generic type and was skipped by the Davish.Sendr source generator. " +
                        "Register it manually with AddRequestHandler/AddStreamRequestHandler instead.",
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
}
