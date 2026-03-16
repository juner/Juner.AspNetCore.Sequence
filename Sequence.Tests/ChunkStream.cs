namespace Juner.AspNetCore.Sequence;

internal class ChunkStream : Stream
{
    readonly byte[] data;
    int position;
    readonly int chunkSize;

    public ChunkStream(byte[] data, int chunkSize)
    {
        this.data = data;
        this.chunkSize = chunkSize;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (position >= data.Length)
            return 0;

        var size = Math.Min(chunkSize, data.Length - position);
        size = Math.Min(size, buffer.Length);

        data.AsSpan(position, size).CopyTo(buffer.Span);

        position += size;

        await Task.Yield();

        return size;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => data.Length;
    public override long Position { get => position; set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}