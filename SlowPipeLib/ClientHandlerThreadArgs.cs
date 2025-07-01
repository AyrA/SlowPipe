using System.Net;
using System.Net.Sockets;

namespace SlowPipeLib;

public record ClientHandlerThreadArgs(Socket Client, IPEndPoint Remote, int? ReceiveBaud, int SendBaud);
