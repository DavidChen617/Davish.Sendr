using System.Runtime.ExceptionServices;

namespace Davish.Sendr.Implements;

/// <summary>
/// Turns an exception thrown synchronously while starting a send into the task the caller gets back,
/// so a send reports failures the same way whether or not a tracing listener is attached.
/// </summary>
/// <remarks>
/// Async on purpose, rather than <see cref="Task.FromException(Exception)"/>: it then produces exactly
/// what the traced path (itself an async method) produces for the same exception — faulted, or canceled
/// for an <see cref="OperationCanceledException"/> — and rethrows the original instance when awaited.
/// </remarks>
internal static class FaultedTask
{
#pragma warning disable CS1998 // Async method lacks 'await' operators: throwing inside it is the point.
    public static async Task From(Exception exception) => ExceptionDispatchInfo.Capture(exception).Throw();

    public static async Task<TResult> From<TResult>(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
        return default!;
    }
#pragma warning restore CS1998
}
