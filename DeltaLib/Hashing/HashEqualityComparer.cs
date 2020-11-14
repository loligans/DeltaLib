
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DeltaLib.Hashing
{
    public class HashEqualityComparer : IEqualityComparer<ReadOnlyMemory<byte>>
    {
        private static EqualityComparer<byte> ByteComparer { get; } = EqualityComparer<byte>.Default;
        public static HashEqualityComparer Default { get; } = new HashEqualityComparer();

        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
        {
            return x.Span.SequenceEqual(y.Span);
        }

        public int GetHashCode([DisallowNull] ReadOnlyMemory<byte> obj)
        {
            unchecked
            {
                var last8 = obj.Span[^8..];
                return HashCode.Combine(last8[0], last8[1], last8[2], last8[3], last8[4], last8[5], last8[6], last8[7]);
            }
        }
    }
}