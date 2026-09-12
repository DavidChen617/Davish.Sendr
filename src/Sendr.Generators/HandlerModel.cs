using System.Collections.Immutable;

namespace Davish.Sendr;

internal enum HandlerKind
{
    Request,
    RequestResponse,
    Stream,
}

/// <summary>
/// A decorator attached to a handler via <c>[Decorate]</c>, already validated against the
/// decorator interface the handler kind requires.
/// </summary>
/// <param name="TypeName">Fully-qualified (global::-prefixed) name of the decorator type.</param>
internal readonly record struct DecoratorRef(string TypeName);

/// <summary>
/// A handler class discovered by the generator, along with everything needed to emit its DI
/// registration and its dispatch-switch arm.
/// </summary>
internal sealed record HandlerModel(
    HandlerKind Kind,
    string RequestType,
    string? ResponseType,
    string HandlerType,
    ImmutableArray<DecoratorRef> Decorators)
{
    public bool Equals(HandlerModel? other) =>
        other is not null &&
        Kind == other.Kind &&
        RequestType == other.RequestType &&
        ResponseType == other.ResponseType &&
        HandlerType == other.HandlerType &&
        Decorators.SequenceEqual(other.Decorators);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Kind.GetHashCode();
            hash = hash * 31 + RequestType.GetHashCode();
            hash = hash * 31 + (ResponseType?.GetHashCode() ?? 0);
            hash = hash * 31 + HandlerType.GetHashCode();
            foreach (var decorator in Decorators)
                hash = hash * 31 + decorator.GetHashCode();
            return hash;
        }
    }
}
