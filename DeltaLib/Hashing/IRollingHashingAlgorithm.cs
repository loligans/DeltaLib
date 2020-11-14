
using System.Buffers;

namespace DeltaLib.Hashing
{
    public interface IRollingHashingAlgorithm
    {
        string Name { get; }
        uint Calculate(ref ReadOnlySequence<byte> block, int blockSize);
        uint Rotate(ref ReadOnlySequence<byte> block, uint checksum, int blockSize);
    }
}