
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DeltaLib.Collections
{
    public class HashEqualityComparer : IEqualityComparer<ReadOnlyMemory<byte>>
    {
        public static HashEqualityComparer Default { get; } = new HashEqualityComparer();

        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
        {
            return x.Span.SequenceEqual(y.Span);
        }

        public int GetHashCode([DisallowNull] ReadOnlyMemory<byte> obj)
        {
            return ((IStructuralEquatable)obj.ToArray()).GetHashCode(EqualityComparer<byte>.Default);
        }
    }
}