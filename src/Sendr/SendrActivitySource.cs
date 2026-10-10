using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Davish.Sendr;

/// <summary>
/// Emits one <see cref="Activity"/> per request/command/query dispatched through
/// <see cref="ISender"/>. Nothing is emitted unless a listener is subscribed to
/// <see cref="Name"/>, e.g. <c>AddSource(SendrActivitySource.Name)</c> in OpenTelemetry.
/// </summary>
/// <remarks>
/// The <c>Invoke</c> methods are public only so the code produced by <c>Davish.Sendr.Generators</c>
/// can call them; application code has no reason to.
/// </remarks>
public static class SendrActivitySource
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name to subscribe to. Part of the public contract: changing
    /// it silently stops existing subscriptions from receiving anything.
    /// </summary>
    public const string Name = "Davish.Sendr";

    internal const string KindTag = "sendr.kind";
    internal const string RequestTypeTag = "sendr.request.type";
    internal const string HandlerTypeTag = "sendr.handler.type";
    internal const string GroupTag = "sendr.notification.group";
    internal const string NotificationKind = "notification";
    internal const string NotificationHandlerKind = "notification.handler";
    internal const string ErrorTypeTag = "error.type";

    private static readonly ActivitySource Source = new(Name);

    // Only touched when a listener is attached, so the string concatenation never runs on the
    // untraced path.
    private static readonly ConcurrentDictionary<(Type Type, string Kind), string> DisplayNames = new();

    /// <summary>
    /// Whether any listener is subscribed. Callers check this first and run their original code
    /// when it is <see langword="false"/>, so the untraced path pays for one check and nothing
    /// else (no state tuple, no delegate), the same way ASP.NET Core's hosting does.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsEnabled => Source.HasListeners();

    /// <summary>Runs <paramref name="call"/> inside an <see cref="Activity"/> when something is listening.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task Invoke<TState>(string kind, Type requestType, TState state, Func<TState, Task> call)
        => Source.HasListeners() ? InvokeTraced(kind, requestType, group: null, state, call) : call(state);

    /// <summary>
    /// Runs one notification handler step (the handler plus its decorators) inside an
    /// <see cref="Activity"/> when something is listening.
    /// </summary>
    /// <param name="handlerType">The concrete handler type.</param>
    /// <param name="group"><c>sequence</c> or <c>parallel</c>.</param>
    /// <param name="state">Passed to <paramref name="call"/> so it can be a non-capturing lambda.</param>
    /// <param name="call">Runs the step.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task InvokeHandler<TState>(Type handlerType, string group, TState state, Func<TState, Task> call)
        => Source.HasListeners() ? InvokeTraced(NotificationHandlerKind, handlerType, group, state, call) : call(state);

    /// <inheritdoc cref="Invoke{TState}(string, Type, TState, Func{TState, Task})"/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<TResult> Invoke<TState, TResult>(
        string kind, Type requestType, TState state, Func<TState, Task<TResult>> call)
        => Source.HasListeners() ? InvokeTraced(kind, requestType, group: null, state, call) : call(state);

    // Async on purpose: Activity.Current is an AsyncLocal, and an async method's changes to it are
    // reverted when control returns to the caller, so the started Activity can't leak into the
    // caller's context. Side effect: while a listener is attached, an exception thrown
    // synchronously by `call` surfaces as a faulted Task instead.
    private static async Task InvokeTraced<TState>(
        string kind, Type requestType, string? group, TState state, Func<TState, Task> call)
    {
        using var activity = Start(kind, requestType, group);
        try
        {
            await call(state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordError(activity, ex);
            throw;
        }
    }

    private static async Task<TResult> InvokeTraced<TState, TResult>(
        string kind, Type requestType, string? group, TState state, Func<TState, Task<TResult>> call)
    {
        using var activity = Start(kind, requestType, group);
        try
        {
            return await call(state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordError(activity, ex);
            throw;
        }
    }

    private static Activity? Start(string kind, Type type, string? group)
    {
        // Null when every listener sampled this one out; `using var` is a no-op for null.
        var activity = Source.StartActivity(DisplayNames.GetOrAdd((type, kind), static key => DisplayPrefix(key.Kind) + key.Type.Name));

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(KindTag, kind);
            activity.SetTag(kind == NotificationHandlerKind ? HandlerTypeTag : RequestTypeTag, type.FullName);

            if (group is not null)
                activity.SetTag(GroupTag, group);
        }

        return activity;
    }

    private static string DisplayPrefix(string kind) => kind switch
    {
        NotificationKind => "Publish ",
        NotificationHandlerKind => "Handle ",
        _ => "Send ",
    };

    private static void RecordError(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetTag(ErrorTypeTag, exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error);
    }
}
