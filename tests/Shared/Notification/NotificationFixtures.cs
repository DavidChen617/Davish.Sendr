using System.Collections.Concurrent;
using System.Diagnostics;
using Davish.Sendr;

namespace Sendr.TestFixtures;

// Compiled into both test projects. In Sendr.Generators.Tests the generator discovers every handler below, so
// a notification's handlers are fixed at compile time; what each handler does in a given test (succeed, fail,
// wait for another handler) comes from the NotificationProbe the test registers.

public enum NoteOutcome
{
    Succeed,
    ThrowWhileStarting,
    FailAsync,
    Cancel,
}

/// <summary>What one test told the notification handlers to do, and what they observed.</summary>
public sealed class NotificationProbe
{
    public static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (NoteOutcome Outcome, Exception? Failure)> _outcomes = [];
    private readonly Dictionary<string, string> _waitFor = [];
    private readonly Dictionary<string, TimeSpan> _delays = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _started = new();

    /// <summary>"&lt;handler&gt; start" and "&lt;handler&gt; end" entries, in the order they happened.</summary>
    public List<string> Log { get; } = [];

    public List<object> HandlersConstructed { get; } = [];
    public List<(string Step, CancellationToken Token)> Tokens { get; } = [];
    public List<ScopedMarker> ScopedSeen { get; } = [];

    /// <summary><see cref="Activity.Current"/> as each handler or decorator saw it while running.</summary>
    public List<(string Step, Activity? Current)> Activities { get; } = [];

    /// <summary>When set, every handler waits for it before finishing.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Makes every handler return an already completed task.</summary>
    public bool CompleteSynchronously { get; set; }

    /// <summary>Makes <paramref name="handler"/> wait this long before finishing.</summary>
    public void Delay(string handler, TimeSpan delay)
    {
        lock (_gate)
            _delays[handler] = delay;
    }

    internal TimeSpan DelayOf(string handler)
    {
        lock (_gate)
            return _delays.GetValueOrDefault(handler);
    }

    /// <summary>Makes the named handler (its class name) end the given way; <paramref name="failure"/> is what it throws.</summary>
    public void Make(string handler, NoteOutcome outcome, Exception? failure = null)
    {
        lock (_gate)
            _outcomes[handler] = (outcome, failure);
    }

    /// <summary>Makes <paramref name="handler"/> wait, after starting, until <paramref name="other"/> has started.</summary>
    public void Await(string handler, string other)
    {
        lock (_gate)
            _waitFor[handler] = other;
    }

    public void Write(string entry)
    {
        lock (_gate)
            Log.Add(entry);
    }

    public void Record<T>(List<T> list, T item)
    {
        lock (_gate)
            list.Add(item);
    }

    public int Count(string entry)
    {
        lock (_gate)
            return Log.Count(e => e == entry);
    }

    internal (NoteOutcome Outcome, Exception? Failure) OutcomeOf(string handler)
    {
        lock (_gate)
            return _outcomes.GetValueOrDefault(handler, (NoteOutcome.Succeed, null));
    }

    internal string? WaitTargetOf(string handler)
    {
        lock (_gate)
            return _waitFor.GetValueOrDefault(handler);
    }

    internal void Started(string handler) => Signal(handler).TrySetResult();

    /// <summary>Completes once <paramref name="handler"/> has started; fails with a TimeoutException if it never does.</summary>
    internal Task StartOf(string handler) => Signal(handler).Task.WaitAsync(SignalTimeout);

    private TaskCompletionSource Signal(string handler) =>
        _started.GetOrAdd(handler, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>A notification handler whose behaviour the test's <see cref="NotificationProbe"/> decides.</summary>
public abstract class ProbeNotificationHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    private readonly NotificationProbe _probe;
    private readonly ScopedMarker _scoped;

    protected ProbeNotificationHandler(NotificationProbe probe, ScopedMarker scoped)
    {
        _probe = probe;
        _scoped = scoped;
        probe.Record(probe.HandlersConstructed, this);
    }

    private string Name => GetType().Name;

    // Not async, so ThrowWhileStarting throws synchronously from HandleAsync itself.
    public Task HandleAsync(TNotification notification, CancellationToken cancellationToken)
    {
        _probe.Record(_probe.Tokens, (Name, cancellationToken));
        _probe.Record(_probe.ScopedSeen, _scoped);
        _probe.Record(_probe.Activities, (Name, Activity.Current));
        _probe.Write($"{Name} start");

        var (outcome, failure) = _probe.OutcomeOf(Name);
        if (outcome == NoteOutcome.ThrowWhileStarting)
            throw failure!;

        return FinishAsync(outcome, failure);
    }

    private async Task FinishAsync(NoteOutcome outcome, Exception? failure)
    {
        _probe.Started(Name);
        if (_probe.WaitTargetOf(Name) is { } other)
            await _probe.StartOf(other);
        if (_probe.Gate is { } gate)
            await gate.Task;
        if (_probe.DelayOf(Name) is var delay && delay > TimeSpan.Zero)
            await Task.Delay(delay);

        if (!_probe.CompleteSynchronously)
            await Task.Yield();

        switch (outcome)
        {
            case NoteOutcome.FailAsync:
                throw failure!;
            case NoteOutcome.Cancel:
                throw new OperationCanceledException();
        }

        _probe.Write($"{Name} end");
    }
}

public sealed class NoteFailure(string message) : Exception(message);

// ---- ProbeNote: four handlers; reflection tests choose which group each goes in ----

public sealed record ProbeNote : INotification;

public sealed class NoteA(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<ProbeNote>(probe, scoped);
public sealed class NoteB(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<ProbeNote>(probe, scoped);
public sealed class NoteC(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<ProbeNote>(probe, scoped);
public sealed class NoteD(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<ProbeNote>(probe, scoped);

// ---- PairedNote: exactly two handlers, for the generated publisher ----

public sealed record PairedNote : INotification;

public sealed class PairedX(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<PairedNote>(probe, scoped);
public sealed class PairedY(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<PairedNote>(probe, scoped);

// ---- DecoratedNote: two handlers, each wrapped by two token-recording decorators ----

public sealed record DecoratedNote : INotification;

[DecorateWith<NoteOuterTokenDecorator, NoteInnerTokenDecorator>]
public sealed class DecoratedX(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<DecoratedNote>(probe, scoped);

[DecorateWith<NoteOuterTokenDecorator, NoteInnerTokenDecorator>]
public sealed class DecoratedY(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<DecoratedNote>(probe, scoped);

public abstract class NoteTokenDecorator(NotificationProbe probe, string step) : INotificationDecorator
{
    public Task HandleAsync<TNotification>(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
        where TNotification : INotification
    {
        probe.Record(probe.Tokens, (step, cancellationToken));
        probe.Record(probe.Activities, (step, Activity.Current));
        return next();
    }
}

public sealed class NoteOuterTokenDecorator(NotificationProbe probe) : NoteTokenDecorator(probe, "outer decorator");

public sealed class NoteInnerTokenDecorator(NotificationProbe probe) : NoteTokenDecorator(probe, "inner decorator");

// ---- BasedNote: one handler; the notification also has an abstract base type ----

public abstract record NoteBase : INotification;
public sealed record BasedNote : NoteBase;

public sealed class BasedNoteHandler(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<BasedNote>(probe, scoped);

// ---- OrphanNote: no handler anywhere ----

public sealed record OrphanNote : INotification;

// ---- Notifications with a step that cannot be resolved ----

/// <summary>Never registered, so anything depending on it fails to resolve.</summary>
public sealed class MissingService;

public sealed record UnresolvableHandlerNote : INotification;

public sealed class MissingDependencyHandler(NotificationProbe probe, ScopedMarker scoped, MissingService missing)
    : ProbeNotificationHandler<UnresolvableHandlerNote>(probe, scoped)
{
    public MissingService Missing { get; } = missing;
}

public sealed class UnresolvableHandlerNotePlain(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<UnresolvableHandlerNote>(probe, scoped);

public sealed record UnresolvableDecoratorNote : INotification;

[DecorateWith<MissingDependencyDecorator>]
public sealed class DecoratorBlockedHandler(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<UnresolvableDecoratorNote>(probe, scoped);

public sealed class UnresolvableDecoratorNotePlain(NotificationProbe probe, ScopedMarker scoped) : ProbeNotificationHandler<UnresolvableDecoratorNote>(probe, scoped);

public sealed class MissingDependencyDecorator(MissingService missing) : INotificationDecorator
{
    public MissingService Missing { get; } = missing;

    public Task HandleAsync<TNotification>(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
        where TNotification : INotification => next();
}
