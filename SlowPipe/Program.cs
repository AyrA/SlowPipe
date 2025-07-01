using SlowPipeLib;
using System.Net.Sockets;

#if DEBUG
args = "/B 56000 /G /L 127.0.0.1:8444 /R 127.0.0.1:80".Split(' ');
#endif

ArgHandler argHandler = new(args);

if (argHandler.IsHelp)
{
    Console.Error.WriteLine(ArgHandler.HelpString);
    return 0;
}

if (argHandler.Mode == OperationMode.Console)
{
    //Local mode
    using var bs = new BaudStream(Console.OpenStandardOutput(), argHandler.BaudRateSend);
    Console.OpenStandardInput().CopyTo(bs);
    return 0;
}
else if (argHandler.Mode == OperationMode.Network)
{
    //Remote mode
    var handler = new ClientHandler();
    if (argHandler.IsGlobalRate)
    {
        handler.ManagerSend = new(argHandler.BaudRateSend);
        handler.ManagerReceive = argHandler.BaudRateReceive.HasValue
            ? new(argHandler.BaudRateReceive.Value)
            : handler.ManagerSend;
        Console.WriteLine("Using global limit of {0} baud for send", argHandler.BaudRateSend);
        if (argHandler.BaudRateReceive.HasValue)
        {
            Console.WriteLine("Using global limit of {0} baud for receive", argHandler.BaudRateReceive);
        }
        else
        {
            Console.WriteLine("Sharing send baud limit with receive limit");
        }
    }
    else
    {
        Console.WriteLine("Use local limit of {0} baud", argHandler.BaudRateSend);
    }
    using var server = new TcpListener(argHandler.Listener!);
    server.Start();
    Console.WriteLine("Waiting for connections on {0}. Press [CTRL]+[C] to exit", argHandler.Listener);
    while (true)
    {
        var t = new Thread(handler.HandleClient)
        {
            IsBackground = true,
        };
        var client = server.AcceptSocket();
        t.Start(new ClientHandlerThreadArgs(
            client,
            argHandler.Receiver!,
            argHandler.BaudRateReceive,
            argHandler.BaudRateSend));
    }
}
else
{
    Console.Error.WriteLine("Invalid arguments. Use '/?' for help");
    return 1;
}
