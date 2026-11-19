using System.Buffers;
using System.Text;

namespace NwcIO.Internal.Export;

internal static class CompatibilityExport
{
    private const int ByteBufferSize = 64 * 1024;
    private const int TextBufferSize = 4096;

    public static Task WriteFileAsync(
        string path, Action<Stream> write, CancellationToken cancellationToken) =>
        AtomicFileWriter.WriteAsync(path, output => new ValueTask(WriteAsync(output, write, cancellationToken)),
            cancellationToken).AsTask();

    public static Task WriteAsync(Stream output, Action<Stream> write, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(output);

            // The legacy formatter runs on a worker. Every destination write still uses cancellable async I/O.
            using var destination = new AsyncStream(output, cancellationToken);
            write(destination);
            destination.Flush();
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);

    public static Task WriteAsync(TextWriter output, Action<TextWriter> write, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            ArgumentNullException.ThrowIfNull(output);
            using var destination = new AsyncTextWriter(output, cancellationToken);
            write(destination);
            destination.Flush();
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);

    public static void WriteText(Stream stream, Encoding encoding, Action<TextWriter> write)
    {
        using var writer = new StreamWriter(stream, encoding, ByteBufferSize, leaveOpen: true);
        write(writer);
    }

    private sealed class AsyncStream(Stream output, CancellationToken token) : Stream
    {
        private byte[]? _buffer;
        private bool _failed;

        public override void Write(byte[] buffer, int offset, int count) => WriteMemory(buffer.AsMemory(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty) return;

            _buffer ??= ArrayPool<byte>.Shared.Rent(ByteBufferSize);
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(buffer.Length, ByteBufferSize);
                buffer[..count].CopyTo(_buffer);
                WriteMemory(_buffer.AsMemory(0, count));
                buffer = buffer[count..];
            }
        }

        private void WriteMemory(ReadOnlyMemory<byte> buffer)
        {
            // StreamWriter and ZipArchive can attempt more writes while unwinding a failure.
            if (_failed) return;
            token.ThrowIfCancellationRequested();
            try
            {
                output.WriteAsync(buffer, token).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                _failed = true;
                throw;
            }
        }

        public override void Flush()
        {
            if (_failed) return;
            token.ThrowIfCancellationRequested();
            try
            {
                output.FlushAsync(token).GetAwaiter().GetResult();
            }
            catch
            {
                _failed = true;
                throw;
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => output.CanSeek;
        public override bool CanWrite => output.CanWrite;
        public override long Length => output.Length;
        public override long Position
        {
            get => output.Position;
            set
            {
                if (_failed) return;
                token.ThrowIfCancellationRequested();
                output.Position = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_failed) return output.Position;
            token.ThrowIfCancellationRequested();
            return output.Seek(offset, origin);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && _buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class AsyncTextWriter : TextWriter
    {
        private readonly TextWriter _output;
        private readonly CancellationToken _token;
        private readonly char[] _buffer = new char[TextBufferSize];
        private int _length;

        public AsyncTextWriter(TextWriter output, CancellationToken token) : base(output.FormatProvider)
        {
            _output = output;
            _token = token;
            NewLine = output.NewLine;
        }

        public override Encoding Encoding => _output.Encoding;

        public override void Write(char value)
        {
            Span<char> single = [value];
            Write(single);
        }

        public override void Write(string? value) => Write(value.AsSpan());

        public override void Write(ReadOnlySpan<char> buffer)
        {
            _token.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(buffer.Length, _buffer.Length - _length);
                buffer[..count].CopyTo(_buffer.AsSpan(_length));
                _length += count;
                buffer = buffer[count..];
                if (_length == _buffer.Length) WriteBuffer();
            }
        }

        public override void Flush()
        {
            WriteBuffer();
            _output.FlushAsync(_token).GetAwaiter().GetResult();
        }

        private void WriteBuffer()
        {
            _token.ThrowIfCancellationRequested();
            if (_length == 0) return;
            _output.WriteAsync(_buffer.AsMemory(0, _length), _token).GetAwaiter().GetResult();
            _length = 0;
        }
    }
}
