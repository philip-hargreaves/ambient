using System.Buffers.Binary;

namespace ClinicAVT.Client;

/// <summary>Each frame is a 4-byte little-endian payload length followed by the payload.</summary>
public static class Framing
{
    // The pipe imposes no size limit, so this cap prevents unbounded allocation
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public const int HeaderBytes = 4;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "frame exceeds MaxFrameBytes");
        }

        var frame = new byte[HeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderBytes));
        return frame;
    }

    public static int ReadDeclaredLength(ReadOnlySpan<byte> header)
    {
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length > MaxFrameBytes)
        {
            throw new InvalidDataException("declared frame length exceeds MaxFrameBytes");
        }

        return (int)length;
    }

    /// <summary>One frame's payload, read whole. Throws when the stream ends first.</summary>
    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderBytes];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var body = new byte[ReadDeclaredLength(header)];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return body;
    }
}
