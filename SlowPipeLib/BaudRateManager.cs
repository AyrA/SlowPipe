using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SlowPipeLib;

/// <summary>
/// Handles baud rate limitations
/// </summary>
public class BaudRateManager
{
    /// <summary>
    /// The estimated number of times we can call <see cref="Thread.Yield"/> per second.
    /// <see cref="RecommendedBufferByteCount"/> is estimated based on this value.
    /// On modern systems this may be set higher because yielding often does nothing
    /// because there's less active threads than there are CPU cores.
    /// </summary>
    /// <remarks>
    /// A sleep of 1 millisecond is enforced,
    /// should yielding be skipped by the system
    /// </remarks>
    private const int ThreadCyclesPerSecond = 500;

    private readonly SemaphoreSlim sem = new(1);
    private readonly Stopwatch sw = Stopwatch.StartNew();
    private readonly bool[] waitSkips = new bool[100];
    private int skipPtr = 0;
    private int baudRate;

    /// <summary>
    /// Gets the number of times the wait function was called,
    /// but a wait was not necessary
    /// </summary>
    /// <remarks>
    /// This is a ringbuffer.
    /// Use <see cref="SkipBacklogSize"/> to get the size of the buffer
    /// </remarks>
    public int WaitSkipCount => waitSkips.Count(m => m);

    /// <summary>
    /// Gets the percentage of calls to <see cref="WaitForBaud"/> that were unnecessary
    /// </summary>
    public int WaitSkipPercentage => (int)Math.Clamp(Math.Round(WaitSkipCount * 100.0 / SkipBacklogSize), 0.0, 100.0);

    /// <summary>
    /// Gets the size of the ring buffer that tracks wait results
    /// </summary>
    public int SkipBacklogSize => waitSkips.Length;

    /// <summary>
    /// Gets the amount of time that has elapsed since the creation of this instance,
    /// or the last call to <see cref="Reset"/>
    /// </summary>
    public TimeSpan BaudTrackStart => sw.Elapsed;

    /// <summary>
    /// Get the recommended size of a byte buffer for the given baud rate
    /// </summary>
    public int RecommendedBufferByteCount => baudRate == 0 ? int.MaxValue : GetRecommendedBufferSize(BaudRate);

    /// <summary>
    /// Gets or sets the baud rate
    /// </summary>
    /// <remarks>
    /// Setting the baud rate to zero disables the component.
    /// Whenever the baud rate is changed, <see cref="Reset"/> should be called.
    /// Changing the baud rate has no effect on any call to <see cref="WaitForBaud"/>
    /// that is already ongoing
    /// </remarks>
    public int BaudRate
    {
        get => baudRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            baudRate = value;
        }
    }

    public BaudRateManager(int baudRate)
    {
        BaudRate = baudRate;
    }

    /// <summary>
    /// Resets the baudrate clock and the skip ringbuffer
    /// </summary>
    /// <remarks>
    /// This should be invoked whenever there are time gaps between calls of <see cref="WaitForBaud"/>,
    /// or when the baud rate was changed
    /// </remarks>
    public void Reset()
    {
        sem.Wait();
        try
        {
            Array.Clear(waitSkips);
            sw.Restart();
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Puts the current thread to sleep to match the given baud rate.
    /// This call hard-limits the rate to the configured value
    /// and will not allow for burst processing.
    /// Use <see cref="WaitForBaud"/> to allow for data burst.
    /// </summary>
    /// <param name="bytesToProcess">
    /// Number of bytes to process at the given rate
    /// </param>
    /// <param name="ct">A cancellation token that can be used to abort the wait operation</param>
    /// <remarks>
    /// This call is thread safe
    /// </remarks>
    public void WaitForInstantaneousBaud(long bytesToProcess, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesToProcess);
        if (baudRate == 0 || bytesToProcess == 0)
        {
            return;
        }
        var delay = ComputeInstantaneousDelay(bytesToProcess);
        if (delay < 0)
        {
            return;
        }
        var sw = Stopwatch.StartNew();
        while (delay > sw.ElapsedMilliseconds)
        {
            sem.Wait(ct);
            try
            {
                YieldOrSleep();
            }
            finally
            {
                sem.Release();
            }
        }
    }

    /// <summary>
    /// Puts the current thread to sleep to match the given baud rate.
    /// This call hard-limits the rate to the configured value
    /// and will not allow for burst processing.
    /// Use <see cref="WaitForBaud"/> to allow for data burst.
    /// </summary>
    /// <param name="bytesToProcess">
    /// Number of bytes to process at the given rate
    /// </param>
    /// <param name="ct">A cancellation token that can be used to abort the wait operation</param>
    /// <remarks>
    /// This call is thread safe
    /// </remarks>
    public async Task WaitForInstantaneousBaudAsync(long bytesToProcess, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesToProcess);
        if (baudRate == 0 || bytesToProcess == 0)
        {
            return;
        }
        var delay = bytesToProcess * 8.0 / baudRate;
        if (delay < 0)
        {
            return;
        }
        sem.Wait(ct);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Puts the current thread to sleep to match the given baud rate
    /// </summary>
    /// <param name="bytesProcessed">
    /// Total number of bytes processed since the creation of this instance
    /// or the last call to <see cref="Reset"/>
    /// </param>
    /// <param name="ct">A cancellation token that can be used to abort the wait operation</param>
    /// <returns>
    /// True if a wait was justified or <paramref name="baudRate"/> is zero.<br />
    /// False if waiting was not necessary.
    /// Use <see cref="WaitSkipCount"/> and <see cref="SkipBacklogSize"/> to calculate the percentage
    /// of unnecessary waits
    /// </returns>
    /// <remarks>
    /// If the result is often "false" it can indicate that the underlying byte processing mechanism
    /// (usually a stream) cannot handle the specified baudrate.
    /// Potential solutions are as follows:
    /// <list type="bullet">
    /// <item>Decrease the requested baud rate</item>
    /// <item>Increase the number of bytes processed in one iteration</item>
    /// <item>Reduce the number of IO wait operations, for example by calling <see cref="Stream.Flush"/> less often</item>
    /// </list>
    /// This call is thread safe
    /// </remarks>
    public bool WaitForBaud(long bytesProcessed, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesProcessed);
        if (baudRate == 0)
        {
            return true;
        }
        var delay = ComputeDelay(bytesProcessed);
        if (delay < 0)
        {
            return !SetSkip(true);
        }
        while (delay > 0)
        {
            sem.Wait(ct);
            try
            {
                YieldOrSleep();
            }
            finally
            {
                sem.Release();
            }
            delay = ComputeDelay(bytesProcessed);
        }
        return !SetSkip(false);
    }

    /// <summary>
    /// Creates a task that completes when the given baud rate is matched.
    /// This is less precise than <see cref="WaitForBaud"/> but will not stall a thread.
    /// If the thread pool is full, tasks may get artificially delayed by the runtime
    /// beyond what is computed here.<br />
    /// On average, the baud rate should match,
    /// but if a successful wait is followed by multiple unsuccessful waits
    /// it means the thread pool can likely no handle the rates anymore.
    /// </summary>
    /// <inheritdoc cref="WaitForBaud(int, int, CancellationToken)"/>
    public async Task<bool> WaitForBaudAsync(long bytesProcessed, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesProcessed);
        if (baudRate == 0)
        {
            return true;
        }
        var delay = ComputeDelay(bytesProcessed);
        if (delay < 0)
        {
            return !SetSkip(true);
        }
        SetSkip(false);

        await sem.WaitAsync(ct);
        try
        {
            //Recompute the delay to account for the semaphore wait
            delay = ComputeDelay(bytesProcessed);
            await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
            return true;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Computes the necessary delay in milliseconds to match the given baud rate
    /// </summary>
    /// <param name="bytesProcessed">Total number of bytes processed</param>
    /// <returns>Delay in milliseconds</returns>
    private double ComputeDelay(long bytesProcessed)
    {
        //Baud rate is the symbol rate.
        //In digital electronics, a symbol is one bit.
        var baud = bytesProcessed * 8.0;
        var milliseconds = baud / baudRate * 1000.0;
        return milliseconds - sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// Computes the necessary delay in milliseconds to match the given baud rate
    /// without considering data burst
    /// </summary>
    /// <param name="bytesProcessed">Total number of bytes processed</param>
    /// <returns>Delay in milliseconds</returns>
    private double ComputeInstantaneousDelay(long bytesProcessed)
    {
        //Baud rate is the symbol rate.
        //In digital electronics, a symbol is one bit.
        var baud = bytesProcessed * 8.0;
        return baud / baudRate * 1000.0;
    }

    private bool SetSkip(bool skip)
    {
        return waitSkips[skipPtr++ % waitSkips.Length] = skip;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void YieldOrSleep()
    {
        if (!Thread.Yield())
        {
            Thread.Sleep(1);
        }
    }

    public static int GetRecommendedBufferSize(int baudRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baudRate);
        return (int)Math.Round(Math.Max(1.0, baudRate * 1.0 / ThreadCyclesPerSecond));
    }
}
