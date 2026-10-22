using System.Buffers.Binary;

namespace M1vClock.Diagnostics.Crashes;

public static class DumpHeaderReader
{
    public const int HeaderLength = 0x60;

    private const uint PageSignature = 0x45474150;
    private const uint Dump64Signature = 0x34365544;
    private const int BugCheckCodeOffset = 0x38;
    private const int BugCheckParametersOffset = 0x40;

    public static BugCheckInfo? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[HeaderLength];
        var read = 0;
        while (read < HeaderLength)
        {
            var chunk = stream.Read(header[read..]);
            if (chunk == 0)
            {
                return null;
            }

            read += chunk;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != PageSignature || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != Dump64Signature)
        {
            return null;
        }

        var code = BinaryPrimitives.ReadUInt32LittleEndian(header[BugCheckCodeOffset..]);
        var parameters = new ulong[4];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i] = BinaryPrimitives.ReadUInt64LittleEndian(header[(BugCheckParametersOffset + (i * 8))..]);
        }

        return new BugCheckInfo(code, parameters);
    }
}
