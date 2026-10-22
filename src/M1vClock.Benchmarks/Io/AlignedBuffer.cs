using System.Buffers;
using System.Runtime.InteropServices;

namespace M1vClock.Benchmarks.Io;

public sealed unsafe class AlignedBuffer : MemoryManager<byte>, IDisposable
{
    public const int SectorAlignment = 4096;

    private void* _pointer;
    private readonly int _length;

    public AlignedBuffer(int length, int alignment = SectorAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(alignment, 1);
        _length = length;
        Alignment = alignment;
        _pointer = NativeMemory.AlignedAlloc((nuint)length, (nuint)alignment);
        NativeMemory.Clear(_pointer, (nuint)length);
    }

    public int Alignment { get; }

    public nint Address => (nint)_pointer;

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_pointer is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, _length);
        return new MemoryHandle((byte*)_pointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_pointer is null)
        {
            return;
        }

        NativeMemory.AlignedFree(_pointer);
        _pointer = null;
    }
}
