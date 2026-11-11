using System;
using System.IO;
using System.Text;

namespace FreeAstroApi;

/// <summary>
/// A non-JSON response body, such as a rendered chart (<c>image/svg+xml</c>, <c>image/png</c>), a PDF or Markdown.
/// </summary>
public sealed class FreeAstroContent
{
    private readonly byte[] _data;

    /// <summary>Creates a response body.</summary>
    /// <param name="data">The raw bytes.</param>
    /// <param name="contentType">The media type, without parameters.</param>
    /// <param name="charset">The charset parameter of the content type, if any.</param>
    /// <param name="fileName">The file name from <c>Content-Disposition</c>, if any.</param>
    public FreeAstroContent(byte[] data, string? contentType, string? charset = null, string? fileName = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ContentType = contentType;
        Charset = charset;
        FileName = fileName;
    }

    /// <summary>The raw bytes.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>The number of bytes.</summary>
    public int Length => _data.Length;

    /// <summary>The media type, for example <c>image/svg+xml</c>.</summary>
    public string? ContentType { get; }

    /// <summary>The charset parameter of the content type, if any.</summary>
    public string? Charset { get; }

    /// <summary>The file name suggested by the <c>Content-Disposition</c> header, if any.</summary>
    public string? FileName { get; }

    /// <summary>Whether the media type is textual (text/*, JSON, XML, SVG).</summary>
    public bool IsText =>
        ContentType is { } type
        && (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
            || type.EndsWith("json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a copy of the bytes.</summary>
    public byte[] ToArray() => (byte[])_data.Clone();

    /// <summary>Opens a read-only stream over the bytes.</summary>
    public Stream OpenRead() => new MemoryStream(_data, writable: false);

    /// <summary>Decodes the body as text using its charset (UTF-8 by default).</summary>
    public string ReadAsString()
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrEmpty(Charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(Charset!.Trim('"'));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }
        return encoding.GetString(_data);
    }

    /// <inheritdoc />
    public override string ToString() => $"{ContentType ?? "application/octet-stream"} ({Length} bytes)";
}
