
using System.Security.Cryptography;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace DeltaLib.Hashing
{
    public class SHA1HashingAlgorithm : IHashingAlgorithm
    {
        public string Name => nameof(SHA1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<byte> ComputeHash(ref ReadOnlySequence<byte> buffer, int blockSize)
        {
            ReadOnlySpan<byte> block = buffer.FirstSpan;
            var blockLength = block.Length;

            // Calculate Hash for a single block (Fast)
            if (buffer.IsSingleSegment || blockLength >= blockSize)
            {
                return SHA1.HashData(block).AsMemory();
            }

            // Calculate Hash for a segmented block (Slower)
            Span<byte> bufferCopy = blockSize <= 2048 ? stackalloc byte[blockSize] : new byte[blockSize];
            var from = 0;
            foreach (var segment in buffer)
            {
                block = segment.Span;
                // Prevent IndexOutOfRangeException when segment length > bufferCopy length
                if (block.Length + from > blockSize)
                {
                    for (var i = 0; i < blockLength; i++)
                    {
                        if (from + i >= blockSize)
                        {
                            return SHA1.HashData(bufferCopy).AsMemory();
                        }
                        bufferCopy[from + i] = block[i];
                    }
                }

                // Copy the memory into the buffer
                block.CopyTo(bufferCopy[from..]);
                from += blockLength;
            }

            return SHA1.HashData(bufferCopy).AsMemory();
        }
    }
}