
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DeltaLib.Models
{
    public record Signature : IEqualityComparer<Signature>, IComparer<Signature>
    {
        public long StartOffset { get; init; }
        public short Length { get; init; }
        public ReadOnlyMemory<byte> Hash { get; init; }
        public uint RollingChecksum { get; init; }

        public int Compare(Signature? x, Signature? y)
        {
            if (x is null) { throw new ArgumentNullException(nameof(x), $"{nameof(x)} cannot be null"); }
            if (y is null) { throw new ArgumentNullException(nameof(y), $"{nameof(y)} cannot be null"); }
            var comparison = x.RollingChecksum.CompareTo(y.RollingChecksum);
            return comparison == 0 ? x.StartOffset.CompareTo(y.StartOffset) : comparison;
        }

        public bool Equals(Signature? x, Signature? y)
        {
            if (x is null && y is null) { return true; }
            if (x is null) { return false; }
            if (y is null) { return false; }
            return x.Hash.Span.SequenceEqual(y.Hash.Span);
        }

        public int GetHashCode([NotNull] Signature obj)
        {
            unchecked
            {
                if (obj!.Hash.Length < 8) { return 0; }
                var last8 = obj.Hash.Span[^8..];
                return HashCode.Combine(last8[0], last8[1], last8[2], last8[3], last8[4], last8[5], last8[6], last8[7]);
            }
        }
    }
}