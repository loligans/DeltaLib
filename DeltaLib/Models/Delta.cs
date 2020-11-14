
using System;

namespace DeltaLib.Models
{
    public record Delta
    {
        public DeltaOperationType OperationType { get; init; }
        public ReadOnlyMemory<byte>? Data { get; init; }
        public uint Checksum { get; init; }
        public long Target { get; init; }
        public long Length { get; init; }
    }

    public enum DeltaOperationType
    {
        Copy = 0,
        Write = 1
    }
}
