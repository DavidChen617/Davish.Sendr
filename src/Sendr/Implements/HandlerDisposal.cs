namespace Davish.Sendr.Implements;

/// <summary>
/// Shared synchronous-disposal logic for the <c>*DecoratorHandler</c> wrapper classes.
/// </summary>
internal static class HandlerDisposal
{
    /// <summary>
    /// Disposes <paramref name="inner"/> synchronously if it supports that, or throws if it only
    /// supports async disposal — mirroring how the DI container's own synchronous scope disposal
    /// behaves for a directly-registered (undecorated) handler that implements only
    /// <see cref="IAsyncDisposable"/>. Without this, a decorated handler would silently skip
    /// cleanup instead of surfacing the same "use an async scope" signal an undecorated one gives.
    /// </summary>
    public static void DisposeSync(object inner)
    {
        switch (inner)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case IAsyncDisposable:
                throw new InvalidOperationException(
                    $"'{inner.GetType()}' only implements IAsyncDisposable and cannot be disposed " +
                    "synchronously. Dispose the scope asynchronously instead (e.g. with `await using`).");
        }
    }
}
