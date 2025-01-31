namespace MareSynchronos.Utils;

// Limits the number of bytes read/written to an underlying stream
public class LimitedStream : Stream
{
    private readonly Stream _stream;
	public long _estimatedPosition = 0;
	public long MaxPosition { get; private init; }
    public bool DisposeUnderlying = true;

    public Stream UnderlyingStream { get => _stream; }

    public LimitedStream(Stream underlyingStream, long byteLimit)
    {
        _stream = underlyingStream;
		try
		{
			_estimatedPosition = Position;
		}
		catch { }
		MaxPosition = _estimatedPosition + byteLimit;
    }

    protected override void Dispose(bool disposing)
    {
        if (!DisposeUnderlying)
            return;
        _stream.Dispose();
    }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => _stream.CanWrite;
    public override long Length => _stream.Length;

    public override long Position { get => _stream.Position; set => _stream.Position = _estimatedPosition = value; }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
		int remainder = (int)long.Clamp(MaxPosition - _estimatedPosition, 0, int.MaxValue);

		if (count > remainder)
			count = remainder;

        int n = _stream.Read(buffer, offset, count);
		_estimatedPosition += n;
		return n;
    }

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
		int remainder = (int)long.Clamp(MaxPosition - _estimatedPosition, 0, int.MaxValue);

		if (count > remainder)
			count = remainder;

        int n = await _stream.ReadAsync(buffer, offset, count, cancellationToken);
		_estimatedPosition += n;
		return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long result = _stream.Seek(offset, origin);
		_estimatedPosition = result;
		return result;
    }

    public override void SetLength(long value)
    {
        _stream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
		int remainder = (int)long.Clamp(MaxPosition - _estimatedPosition, 0, int.MaxValue);

		if (count > remainder)
			count = remainder;

        _stream.Write(buffer, offset, count);
		_estimatedPosition += count;
    }

    public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
		int remainder = (int)long.Clamp(MaxPosition - _estimatedPosition, 0, int.MaxValue);

		if (count > remainder)
			count = remainder;

        await _stream.WriteAsync(buffer, offset, count, cancellationToken);
		_estimatedPosition += count;
    }
}
