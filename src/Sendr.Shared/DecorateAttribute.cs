// ReSharper disable UnusedTypeParameter
namespace Davish.Sendr;

/// <summary>
/// Declares that a handler discovered by the Davish.Sendr source generator should be wrapped
/// with the given decorator(s) when <c>AddSendrGenerated</c> composes its pipeline, for example
/// <c>[Decorate&lt;TransactionDecorator, LoggingDecorator&gt;]</c>. Type arguments are applied
/// outer to inner — the first one runs first, matching <c>x.Decorator.With&lt;T&gt;()</c>
/// ordering — and each must implement <c>IRequestDecorator</c>, <c>IRequestDecorator.WithResponse</c>,
/// or <c>IStreamRequestDecorator</c> as appropriate for the handler being decorated (defined in
/// Davish.Sendr.Abstractions, which this shared project doesn't reference, hence plain <c>&lt;c&gt;</c>
/// text here instead of a resolvable <c>cref</c>). Apply at most one <c>[Decorate&lt;...&gt;]</c>
/// per class; pick the overload with the arity you need (1 through 8).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3, T4> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3, T4, T5> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3, T4, T5, T6> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3, T4, T5, T6, T7> : Attribute;

/// <inheritdoc cref="DecorateAttribute{T1}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecorateAttribute<T1, T2, T3, T4, T5, T6, T7, T8> : Attribute;
