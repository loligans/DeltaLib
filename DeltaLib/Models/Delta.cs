
namespace DeltaLib.Models
{
    public record Delta
    {
        public DeltaOperationType OperationType { get; init; }
        public int TargetBlockIndex { get; init; }
        public int SourceBlockIndex { get; init; }
    }

    public enum DeltaOperationType
    {
        Copy = 1,
        Write = 2,
        Delete = 3
    }

}
