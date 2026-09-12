namespace Davish.Sendr.Implements;

/// <summary>
/// Builds the exception thrown when no handler is registered for a request/command/query type,
/// naming the concrete registration call needed instead of leaking the DI container's generic
/// "no service registered" message for an internal handler interface type.
/// </summary>
internal static class HandlerResolutionException
{
    public static InvalidOperationException NoHandler(string registrationMethod, Type requestType) =>
        new($"Davish.Sendr: no handler is registered for '{requestType}'. " +
            $"Call services.{registrationMethod}<{requestType.Name}, YourHandler>() during startup.");

    public static InvalidOperationException NoHandler(string registrationMethod, Type requestType, Type responseType) =>
        new($"Davish.Sendr: no handler is registered for '{requestType}' with response '{responseType}'. " +
            $"Call services.{registrationMethod}<{requestType.Name}, {responseType.Name}, YourHandler>() during startup.");
}
