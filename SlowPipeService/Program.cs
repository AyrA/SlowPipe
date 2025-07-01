using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlowPipeLib;
using SlowPipeService;

#if DEBUG
args = "/B 56000 /G /L 127.0.0.1:8444 /R 192.168.1.31:1080".Split(' ');
#endif

var argHandler = new ArgHandler(args);
if (argHandler.IsHelp)
{
    Console.Error.WriteLine(ArgHandler.HelpString);
    return;
}
if (argHandler.Mode != OperationMode.Network)
{
    Console.Error.WriteLine("Service can only be run in network mode");
}

var builder = new HostBuilder();
builder.UseConsoleLifetime();
builder.UseWindowsService(opt =>
{
    opt.ServiceName = "Slow Pipe Service";
});
builder.ConfigureServices(services =>
{
    services
        .AddSingleton(argHandler)
        .AddHostedService<ServiceContainer>()
        .AddLogging(opt => { opt.AddConsole(); });
});
using var app = builder.Build();
app.Run();