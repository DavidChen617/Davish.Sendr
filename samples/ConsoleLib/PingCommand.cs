using Davish.Sendr;

namespace ConsoleLib;

public sealed record PingCommand : ICommand;

public sealed record PinedNotification : INotification;

public sealed class PingCommandHandler : ICommandHandler<PingCommand>
{
    public Task HandleAsync(PingCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine("Pong!");

        return Task.CompletedTask;
    }
}

public sealed class PinedNotificationHandler : INotificationHandler<PinedNotification>
{
    public Task HandleAsync(PinedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine("Ping received!");
        return Task.CompletedTask;
    }
}
