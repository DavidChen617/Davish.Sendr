using ConsoleLib;
using Davish.Sendr;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSendr(o => o.UseGenerators(
    x => x.IncludeAssemblyOf<IAssemblyMarker>()
));

services.AddSendrNotification(o => o.UseGenerators(
    x => x.IncludeAssemblyOf<IAssemblyMarker>()
));

var provider = services.BuildServiceProvider();

var sender = provider.GetService<ISender>()!;

var publisher = provider.GetService<IPublisher>()!;

await sender.SendAsync(new PingCommand(), CancellationToken.None);

var reply = await sender.SendAsync(new PingQuery(), CancellationToken.None);

Console.WriteLine(reply);

await publisher.PublishAsync(new PinedNotification(), CancellationToken.None);

