using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlowPipeLib;
using System.Net.Sockets;

namespace SlowPipeService;

internal class ServiceContainer(ArgHandler argHandler, ILogger<ServiceContainer> logger) : IHostedService
{
    private CancellationTokenSource cts = new();
    private ClientHandler handler = new();
    private TcpListener server = null!;

    private Thread ThreadHandler(ClientHandlerThreadArgs threadArgs)
    {
        var ep = threadArgs.Client.RemoteEndPoint;
        logger.LogInformation("New client {EndPoint}", ep);
        var t = new Thread((o) =>
        {
            handler.HandleClient(o);
            logger.LogInformation("Tunnel end {EndPoint}", ep);
        })
        {
            IsBackground = true,
        };
        t.Start(threadArgs);
        return t;
    }

    private async void BeginAccept()
    {
        var token = cts.Token;
        while (!token.IsCancellationRequested)
        {
            Socket? client = null;
            try
            {
                client = await server.AcceptSocketAsync(token);
                ThreadHandler(new ClientHandlerThreadArgs(
                    client,
                    argHandler.Receiver!,
                    argHandler.BaudRateReceive,
                    argHandler.BaudRateSend));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to accept socket");
                client?.Dispose();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cts = new();
        handler = new();

        if (argHandler.IsGlobalRate)
        {
            handler.ManagerSend = new(argHandler.BaudRateSend);
            handler.ManagerReceive = argHandler.BaudRateReceive.HasValue
                ? new(argHandler.BaudRateReceive.Value)
                : handler.ManagerSend;
            logger.LogInformation("Using global limit of {BaudRate} baud for send", argHandler.BaudRateSend);
            if (argHandler.BaudRateReceive.HasValue)
            {
                logger.LogInformation("Using global limit of {BaudRate} baud for receive", argHandler.BaudRateReceive);
            }
            else
            {
                logger.LogInformation("Sharing send baud limit with receive limit");
            }
        }
        else
        {
            logger.LogInformation("Use local limit of {BaudRate} baud", argHandler.BaudRateSend);
        }
        server?.Dispose();
        server = new TcpListener(argHandler.Listener!);
        server.Start();
        logger.LogInformation("Listening on {EndPoint}, service ready.", argHandler.Listener);
        BeginAccept();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        server?.Dispose();
        cts.Cancel();
        return Task.CompletedTask;
    }
}
