using System.Buffers.Binary;

namespace Ambient.Client.Tests;

public class FramingTest
{
    [Fact]
    public void AFrameIsALittleEndianLengthThenTheBodyAndTheCapIsInclusive()
    {
        var frame = Framing.Encode("hi"u8);

        Assert.Equal(Framing.HeaderBytes + 2, frame.Length);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(frame));
        Assert.Equal("hi"u8.ToArray(), frame[Framing.HeaderBytes..]);
        Assert.Equal(2, Framing.ReadDeclaredLength(frame));

        var capped = Framing.Encode(new byte[Framing.MaxFrameBytes]);
        Assert.Equal(Framing.MaxFrameBytes, Framing.ReadDeclaredLength(capped));

        var oversize = new byte[Framing.MaxFrameBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => Framing.Encode(oversize));

        var header = new byte[Framing.HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)Framing.MaxFrameBytes + 1);
        Assert.Throws<InvalidDataException>(() => Framing.ReadDeclaredLength(header));
    }
}
