
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DeltaLib.Models
{
    public record Delta : Signature
    {
        public DeltaOperationType OperationType { get; init; }
        public ReadOnlyMemory<byte>? Data { get; init; }
        public long? Source { get; init; }
    }

    public enum DeltaOperationType
    {
        /// <summary>
        /// sdf
        /// </summary>
        Copy = 0,
        /// <summary>
        /// asdf
        /// </summary>
        Write = 1,
        Delete = 3
    }
}
