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

    // messageInterface is the open message interface the request was sent as, e.g. typeof(IQuery<>). It tells a
    // missing registration apart from a covariant upcast, which no registration fixes.
    public static InvalidOperationException NoHandler(
        string registrationMethod, Type requestType, Type responseType, Type messageInterface) =>
        new(CovariantUpcast(requestType, responseType, messageInterface) ??
            $"Davish.Sendr: no handler is registered for '{requestType}' with response '{responseType}'. " +
            $"Call services.{registrationMethod}<{requestType.Name}, {responseType.Name}, YourHandler>() during startup.");

    // TResponse is covariant, so a message declaring IQuery<string> can be sent through an IQuery<object>
    // variable. The lookup then asks for a response type the message never declared. The generated sender
    // (SourceBuilder) emits the same check and wording.
    private static string? CovariantUpcast(Type requestType, Type responseType, Type messageInterface)
    {
        var declared = requestType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == messageInterface)
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        if (declared.Count == 0 || declared.Contains(responseType))
            return null;

        var name = messageInterface.Name.Substring(0, messageInterface.Name.IndexOf('`'));
        var request = Display(requestType);
        var response = Display(responseType);
        return $"Davish.Sendr: no handler is found for '{requestType}' with response '{responseType}': " +
               $"'{request}' declares {string.Join(", ", declared.Select(t => $"{name}<{Display(t)}>"))}, " +
               $"not {name}<{response}>. It was sent as {name}<{response}> (a covariant upcast), " +
               $"so the handler was looked up with '{response}' as the response type. " +
               $"Send it as '{request}' or as an interface it declares.";
    }

    // List`1 written as List<String>, so a generic response type reads the way it does in code.
    private static string Display(Type type)
    {
        var arity = type.Name.IndexOf('`');
        return type.IsGenericType && arity >= 0
            ? $"{type.Name.Substring(0, arity)}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>"
            : type.Name;
    }
}
