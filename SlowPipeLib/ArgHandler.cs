using System.ComponentModel.DataAnnotations;
using System.Net;

namespace SlowPipeLib;

public class ArgHandler
{
    public OperationMode Mode { get; }

    public bool IsHelp { get; }

    public int BaudRateSend { get; }

    public int? BaudRateReceive { get; }

    public bool IsGlobalRate { get; }

    public IPEndPoint? Listener { get; }

    public IPEndPoint? Receiver { get; }

    public ArgHandler(params string[] args)
    {
        if (args == null || args.Length == 0 || args.Contains("/?") || args.Contains("--help", StringComparer.InvariantCultureIgnoreCase) || args.Contains("-h", StringComparer.InvariantCultureIgnoreCase) || args.Contains("-?"))
        {
            IsHelp = true;
            return;
        }
        if (args.Length == 1)
        {
            Mode = OperationMode.Console;
            BaudRateSend = int.Parse(args[0]);
            return;
        }

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToUpperInvariant())
            {
                case "/B":
                    if (BaudRateSend > 0 || BaudRateReceive.HasValue)
                    {
                        throw new ArgumentException("Baud rate already specified");
                    }
                    BaudRateSend = int.Parse(args[++i]);
                    break;
                case "/BS":
                    if (BaudRateSend > 0)
                    {
                        throw new ArgumentException("Baud rate already specified");
                    }
                    BaudRateSend = int.Parse(args[++i]);
                    break;
                case "/BR":
                    if (BaudRateReceive.HasValue)
                    {
                        throw new ArgumentException("Baud rate already specified");
                    }
                    BaudRateReceive = int.Parse(args[++i]);
                    break;
                case "/R":
                    Mode = OperationMode.Network;
                    if (Receiver != null)
                    {
                        throw new ArgumentException("Forward address already specified");
                    }
                    Receiver = IPEndPoint.Parse(args[++i]);
                    break;
                case "/L":
                    Mode = OperationMode.Network;
                    if (Listener != null)
                    {
                        throw new ArgumentException("Listener address already specified");
                    }
                    Listener = IPEndPoint.Parse(args[++i]);
                    break;
                case "/G":
                    if (IsGlobalRate)
                    {
                        throw new ArgumentException("/G already specified");
                    }
                    IsGlobalRate = true;
                    break;
                default:
                    throw new ArgumentException($"Invalid command line argument: '{args[i]}'");

            }
        }
        Validate();
    }

    public void Validate()
    {
        if (IsHelp)
        {
            return;
        }

        if (Mode == OperationMode.None)
        {
            throw new ValidationException("Operation mode could not be determined");
        }

        if (BaudRateSend <= 0)
        {
            throw new ValidationException("Baud rate must be greater than zero");
        }
        if (BaudRateReceive.HasValue && BaudRateReceive <= 0)
        {
            throw new ValidationException("Baud rate must be greater than zero");
        }
        if (Listener != null || Receiver != null)
        {
            if (Receiver == null || Listener == null)
            {
                throw new ValidationException("Cannot specify a listener or forward address by itself, both addresses must be specified");
            }
            if (Mode != OperationMode.Network)
            {
                throw new ValidationException("If a listener or forward address is specified, 'Network' mode must also be specified");
            }
        }
        if (Mode == OperationMode.Console)
        {
            if (BaudRateReceive.HasValue)
            {
                throw new ValidationException("Receiving baud rate is only applicable to network mode operation");
            }
        }
    }

    public static readonly string HelpString = @"SlowPipe [/B[S]] <baud>
SlowPipe /B <baud> /L <local> /R <remote> [/G]
SlowPipe /BR <baud-rec> /BS <baud-send> /L <local> /R <remote> [/G]

Local mode
==========
With just the baud rate, the application operates in local mode.
It simply forwards data from stdin to stdout.
/B or /BS is optional in this case.

baud: Pipe data received on stdin to stdout using the given baud rate
      Baud rate in this instance means bits per second.

Remote mode
===========
With 3 or 4 arguments, the application operates in remote mode.
It listens on the given IP:Port combination using TCP.
Every time a connection is received, a connection to the forward location is
made and all data sent and received is throttled accordingly

baud:       Baud rate to forward data in either direction
baud-rec:   Baud rate to read data as (remote to local)
baud-send:  Baud rate to send data as (local to remote)

local:     IP:port combination to listen on

remote:    IP:port combination to connect and forward data to

/G:    Apply baud rate limit globally accross all connections

If two baud rates are specified (even if identical),
they operate independently, if one rate is specified,
it is shared between send and receive.";
}

public enum OperationMode
{
    None,
    Console,
    Network
}