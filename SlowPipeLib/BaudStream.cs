namespace SlowPipeLib;

public class BaudStream : Stream
{
    private readonly Stream baseStream;
    private readonly BaudRateManager baudRateManager;
    private int blockSize = 1;
    private long totalBytesProcessed = 0;

    #region Base Properties

    public override bool CanRead => baseStream.CanRead;

    public override bool CanSeek => baseStream.CanSeek;

    public override bool CanWrite => baseStream.CanWrite;

    public override long Length => baseStream.Length;

    public override long Position { get => baseStream.Position; set => baseStream.Position = value; }

    #endregion

    /// <summary>
    /// Gets or sets the baud rate
    /// </summary>
    /// <remarks>A value of zero disabled the speed limit</remarks>
    public int BaudRate
    {
        get => baudRateManager.BaudRate;
        set => baudRateManager.BaudRate = value;
    }

    /// <summary>
    /// Gets or sets whether data bursts are allowed or not
    /// </summary>
    /// <remarks>
    /// If allowed, the average baud rate after all data has been written will closely
    /// match the desired rate, even after long periods of silence.<br />
    /// If not allowed, every write is constrained to the baud rate.
    /// The average rate will be lower than the desired rate
    /// but it more closely matches the real world.
    /// </remarks>
    public bool AllowBurst { get; set; }

    /// <summary>
    /// Gets the recommended minimum value for the "count" parameter to <see cref="Read(byte[], int, int)"/>.
    /// The actual value should not exceed this by too much
    /// or the transferred data will start to appear in visible chunks.
    /// </summary>
    public int RecommendedReadBufferSize => baudRateManager.RecommendedBufferByteCount;

    #region Ctor

    public BaudStream(Stream baseStream) : this(baseStream, 9600)
    {
        //NOOP
    }

    public BaudStream(Stream baseStream, int baudRate) : this(baseStream, new BaudRateManager(baudRate))
    {
        //NOOP
    }

    public BaudStream(Stream baseStream, BaudRateManager baudRateManager)
    {
        this.baseStream = baseStream;
        this.baudRateManager = baudRateManager;
        blockSize = Math.Min(baudRateManager.RecommendedBufferByteCount, 4096);
    }

    #endregion

    #region Base Methods

    public override void Flush() => baseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer, offset, count, default);

    public override long Seek(long offset, SeekOrigin origin) =>
        baseStream.Seek(offset, origin);

    public override void SetLength(long value) =>
        baseStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer, offset, count, default);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            baseStream.Dispose();
        }
    }

    #endregion

    public int Read(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int read = baseStream.Read(buffer, offset, count);
        totalBytesProcessed += read;
        if (read > 0)
        {
            if (AllowBurst)
            {
                baudRateManager.WaitForBaud(totalBytesProcessed, ct);
            }
            else
            {
                baudRateManager.WaitForInstantaneousBaud(read, ct);
            }
        }
        return read;
    }

    public void Write(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return;
        }
        if (BaudRate == 0)
        {
            baseStream.Write(buffer, offset, count);
            return;
        }
        int written = 0;
        int changeSkip = 10;
        while (count > 0)
        {
            var block = Math.Min(count, blockSize);
            baseStream.Write(buffer, offset + written, block);
            count -= block;
            written += block;
            totalBytesProcessed += block;
            if (AllowBurst)
            {
                if (!baudRateManager.WaitForBaud(totalBytesProcessed, ct))
                {
                    //Increase block size if more than 20% of all wait calls are unnecessary
                    if (baudRateManager.WaitSkipPercentage > 5 && changeSkip <= 0)
                    {
                        //Increase block size by 10%, but at least 1 byte
                        blockSize += Math.Max(1, blockSize / 10);
                        changeSkip = 50;
                    }
                }
            }
            else
            {
                baudRateManager.WaitForInstantaneousBaud(block, ct);
            }

            changeSkip = Math.Max(0, changeSkip - 1);
        }
    }
}
