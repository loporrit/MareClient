namespace MareSynchronos.Utils;

// Counts the number of bytes read/written to an underlying stream
public class CountedStream : Stream
{
    private readonly Stream _stream;
    public long BytesRead { get; private set; }
    public long BytesWritten { get; private set; }
    public bool DisposeUnderlying = true;

    public Stream UnderlyingStream { get => _stream; }

    public CountedStream(Stream underlyingStream)
    {
        _stream = underlyingStream;
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

    public override long Position { get => _stream.Position; set => _stream.Position = value; }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _stream.Read(buffer, offset, count);
        BytesRead += n;
        return n;
    }

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int n = await _stream.ReadAsync(buffer, offset, count, cancellationToken);
        BytesRead += n;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _stream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
        BytesWritten += count;
    }

    public async override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(buffer, offset, count, cancellationToken);
        BytesWritten += count;
    }
}
