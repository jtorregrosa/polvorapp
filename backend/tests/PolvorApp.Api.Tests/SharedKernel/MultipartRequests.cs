using System.Text;
using Microsoft.AspNetCore.Http;

namespace PolvorApp.Api.Tests.SharedKernel;

/// <summary>Hand-built <c>multipart/form-data</c> requests for the upload readers.</summary>
internal static class MultipartRequests
{
    public const string Boundary = "synthetic-boundary";

    /// <summary>A form with file parts, each sent with the file name <c>synthetic.bin</c>.</summary>
    public static HttpRequest Files(params (string Name, byte[] Content)[] parts) => Files(seekable: true, parts);

    public static HttpRequest Files(bool seekable, params (string Name, byte[] Content)[] parts) =>
        Form(seekable, parts, []);

    /// <summary>A form with file parts and plain text fields.</summary>
    public static HttpRequest Form((string Name, byte[] Content)[] files, (string Name, string Value)[] fields) =>
        Form(seekable: true, files, fields);

    public static HttpRequest Request(string contentType, byte[] body, bool seekable = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = contentType;
        context.Request.ContentLength = body.Length;
        context.Request.Body = seekable ? new MemoryStream(body) : new ForwardOnlyStream(body);
        return context.Request;
    }

    private static HttpRequest Form(bool seekable, (string Name, byte[] Content)[] files, (string Name, string Value)[] fields)
    {
        using var stream = new MemoryStream();
        foreach (var (name, value) in fields)
        {
            stream.Write(Encoding.UTF8.GetBytes(
                $"--{Boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n"));
        }

        foreach (var (name, content) in files)
        {
            stream.Write(Encoding.ASCII.GetBytes(
                $"--{Boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"synthetic.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n"));
            stream.Write(content);
            stream.Write("\r\n"u8);
        }

        stream.Write(Encoding.ASCII.GetBytes($"--{Boundary}--\r\n"));
        return Request($"multipart/form-data; boundary={Boundary}", stream.ToArray(), seekable);
    }

    /// <summary>A body that can only be read forwards, like a network stream.</summary>
    private sealed class ForwardOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
