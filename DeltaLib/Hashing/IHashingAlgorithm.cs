
using System;
using System.Buffers;

namespace DeltaLib.Hashing
{
    public interface IHashingAlgorithm
    {
        string Name { get; }
        ReadOnlyMemory<byte> ComputeHash(ref ReadOnlySequence<byte> buffer, int blockSize);
    }
}