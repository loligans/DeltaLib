
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace DeltaLib.Hashing
{
    public class Adler32HashingAlgorithm : IRollingHashingAlgorithm
    {
        public string Name => "Adler32";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Calculate(ref ReadOnlySequence<byte> block, int blockSize)
        {
            ReadOnlySpan<byte> blockSpan = block.FirstSpan;
            var blockLength = blockSpan.Length;
            var a = 1;
            var b = 0;
            byte z;
            // Calculate Checksum for a single block (Fast)
            if (block.IsSingleSegment || blockLength >= blockSize)
            {
                for (var i = 0; i < blockLength; i++)
                {
                    z = blockSpan[i];
                    a = (ushort)(z + a);
                    b = (ushort)(b + a);
                }
            }
            // Calculate Checksum for a segmented block (Slower)
            else
            {
                foreach(var segment in block)
                {
                    blockSpan = segment.Span;
                    blockLength = blockSpan.Length;
                    for (var i = 0; i < blockLength; i++)
                    {
                        z = blockSpan[i];
                        a = (ushort)(z + a);
                        b = (ushort)(b + a);
                    }
                }
            }

            return (uint)((b << 16) | a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Rotate(ref ReadOnlySequence<byte> block, uint checksum, int blockSize)
        {
            ReadOnlySpan<byte> blockSpan = block.FirstSpan;
            var blockLength = blockSpan.Length;
            var b = (ushort)(checksum >> 16 & 0xffff);
            var a = (ushort)(checksum & 0xffff);
            byte remove;
            byte add;

            // Rotate Checksum for a single block (Fast)
            if (block.IsSingleSegment || blockLength >= blockSize)
            {
                remove = blockSpan[0];
                add = blockSpan[^1];
            }
            // Rotate Checksum for a segmented block (Slower)
            else
            {
                var reader = new SequenceReader<byte>(block);
                reader.TryPeek(out remove);
                reader.Advance(blockSize);
                reader.TryPeek(out add);
            }

            a = (ushort)(a - remove + add);
            b = (ushort)(b - (blockSize * remove) + a - 1);

            return (uint)((b << 16) | a);
        }
    }
}