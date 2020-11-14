
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace DeltaLib.Hashing
{
    public class SHA384HashingAlgorithm : IHashingAlgorithm
    {
        public string Name => nameof(SHA384);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<byte> ComputeHash(ref ReadOnlySequence<byte> buffer, int blockSize)
        {
            ReadOnlySpan<byte> block = buffer.FirstSpan;
            var blockLength = block.Length;

            // Calculate Hash for a single block (Fast)
            if (buffer.IsSingleSegment || blockLength >= blockSize)
            {
                return SHA384.HashData(block).AsMemory();
            }

            // Calculate Hash for a segmented block (Slower)
            Span<byte> bufferCopy = blockSize <= 2048 ? stackalloc byte[blockSize] : new byte[blockSize];
            var from = 0;
            foreach (var segment in buffer)
            {
                block = segment.Span;
                blockLength = block.Length;
                // Prevent IndexOutOfRangeException when buffer is > blockSize
                if (blockLength + from > blockSize)
                {
                    for (var i = 0; i < blockLength; i++)
                    {
                        if (from + i >= blockSize)
                        {
                            return SHA384.HashData(bufferCopy).AsMemory();
                        }
                        bufferCopy[from + i] = block[i];
                    }
                }

                block.CopyTo(bufferCopy[from..]);
                from += blockLength;
            }
            
            return SHA384.HashData(bufferCopy).AsMemory();
        }
    }
}