
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DeltaLib.Models
{
    public record Signature
    {
        /// <summary>
        /// The hash of this <see cref="Signature"/>
        /// </summary>
        public ReadOnlyMemory<byte> Hash { get; init; }
        /// <summary>
        /// The checksum of this <see cref="Signature"/>
        /// </summary>
        public uint Checksum { get; init; }
        /// <summary>
        /// The starting point of this <see cref="Signature"/>
        /// </summary>
        public long Target { get; init; }
        /// <summary>
        /// The number of bytes from the <see cref="Target"/> used to calculate this <see cref="Signature"/>.
        /// </summary>
        public long Length { get; init; }
    }

    public class SignatureEqualityComparer : IEqualityComparer<Signature>
    {
        public static SignatureEqualityComparer Default { get; } = new SignatureEqualityComparer();
        public bool Equals(Signature? x, Signature? y)
        {
            if (x is null && y is null) { return true; }
            if (x is null) { return false; }
            if (y is null) { return false; }

            return x.Hash.Span.SequenceEqual(y.Hash.Span);
        }

        public int GetHashCode(Signature? obj)
        {
            if (obj is null) { return 0; }
            return obj.Checksum.GetHashCode();
        }
    }
}