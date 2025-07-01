using System.Net.Sockets;

namespace SlowPipeLib;
public class ClientHandler
{
    private readonly int tunnelCount = 0;

    public BaudRateManager? ManagerSend { get; set; }

    public BaudRateManager? ManagerReceive { get; set; }

    public int TunnelCount => tunnelCount;

    public void HandleClient(object? threadArg)
    {
        ArgumentNullException.ThrowIfNull(threadArg);
        var args = (ClientHandlerThreadArgs)threadArg;
        using var client = args.Client;

        using var dest = new TcpClient();
        dest.Connect(args.Remote);

        //Set up streams
        using var NSRemote = new NetworkStream(dest.Client);
        using var NSLocal = new NetworkStream(client);

        //Set up baud rate managers
        var sendManager = ManagerSend ?? new BaudRateManager(args.SendBaud);
        var receiveManager = ManagerReceive ?? (args.ReceiveBaud.HasValue ? new BaudRateManager(args.ReceiveBaud.Value) : sendManager);

        //Cross-connect streams and wait for end
        using var s1 = new BaudStream(NSRemote, sendManager);
        using var s2 = new BaudStream(NSLocal, receiveManager);
        var t1 = Copy(NSLocal, s1, sendManager.RecommendedBufferByteCount * 2);
        var t2 = Copy(NSRemote, s2, receiveManager.RecommendedBufferByteCount * 2);
        WaitAny(t1, t2);
    }

    private static void WaitAny(params Thread[] threads)
    {
        while (true)
        {
            foreach (var t in threads)
            {
                try
                {
                    if (t.Join(10))
                    {
                        return;
                    }
                }
                catch
                {
                    return;
                }
            }
        }
    }

    private static Thread Copy(Stream from, Stream to, int bufferSize)
    {
        var t = new Thread(() => { try { from.CopyTo(to, bufferSize); } catch { } })
        {
            IsBackground = true
        };
        t.Start();
        return t;
    }
}
