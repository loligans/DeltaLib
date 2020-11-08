using System.Collections.Generic;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using System.Threading;
using System.Security.Cryptography;
using System.Linq;
using DeltaLib.Collections;
using DeltaLib.Models;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace DeltaLib
{
    public interface ISignatureMap
    {
        int BlockCount();
        ReadOnlyMemory<byte>? GetHash(int blockIndex);
        int? GetBlockIndex(ReadOnlyMemory<byte> hash);

        /// <summary>
        /// Creates a two-way mapping of block index and block hash.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> cannot be null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="input"/> must be readable.</exception>
        public Task<ISignatureMap> CreateMap(Stream input, CancellationToken cancellationToken = default);

        /// <summary>
        ///
        /// </summary>
        public Task<IReadOnlyCollection<Delta>> CreateDelta(ISignatureMap comparer, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Calculates the blocks that have changed in a file
    /// </summary>
    public class BinarySignatureMap : ISignatureMap
    {
        private readonly BidirectionalDictionary<int, ReadOnlyMemory<byte>> _signatureMapping;
        public int BlockCount() => _signatureMapping.FirstKeys.Count;
        public int BufferSize { get; init; } = 4194304;
        public int MinimumReadSize { get; init; } = 4194304;

        public BinarySignatureMap() : this(new BidirectionalDictionary<int, ReadOnlyMemory<byte>>()) { }
        public BinarySignatureMap(BidirectionalDictionary<int, ReadOnlyMemory<byte>> signatureMapping)
        {
            if (signatureMapping is null) { throw new ArgumentNullException(nameof(signatureMapping)); }
            if (   signatureMapping.FirstKeyComparer is not EqualityComparer<int>
                || signatureMapping.SecondKeyComparer is not HashEqualityComparer)
            {
                _signatureMapping = new BidirectionalDictionary<int, ReadOnlyMemory<byte>>(
                    signatureMapping,
                    EqualityComparer<int>.Default,
                    HashEqualityComparer.Default);
            }
            else
            {
                _signatureMapping = signatureMapping;
            }
        }

        public async Task<ISignatureMap> CreateMap(Stream input, CancellationToken cancellationToken = default)
        {
            if (input is null) { throw new ArgumentNullException(nameof(input)); }
            if (input.CanRead is false) { throw new InvalidOperationException($"{nameof(input)} cannot be read"); }

            var readerOptions = new StreamPipeReaderOptions(null, BufferSize, MinimumReadSize);
            var inputReader = PipeReader.Create(input, readerOptions);

            // Calculate the hash of the Input
            var blockIndex = 0;
            ReadResult result;
            do
            {
                // Get the next block of data
                result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                // Store the hash and block index
                var hash = ComputeHash(result.Buffer.FirstSpan);
                _signatureMapping.Add(blockIndex, hash);

                // Continue on to the next block
                inputReader.AdvanceTo(result.Buffer.End);
                blockIndex += 1;
            } while (!result.IsCompleted);

            await inputReader.CompleteAsync().ConfigureAwait(false);
            return this;
        }

        public async Task<IReadOnlyCollection<Delta>> CreateDelta(ISignatureMap comparer, CancellationToken cancellationToken = default)
        {
            var input = this;
            if (input is null) { throw new InvalidOperationException("Cannot create delta when no signature map has been created."); }
            if (comparer is null) { throw new ArgumentNullException(nameof(comparer), "Cannot create delta without a comparer map."); }

            return await Task.Run(() => {
                var inputBlockCount = input.BlockCount();
                var compareBlockCount = comparer.BlockCount();
                var maxBlockCount = inputBlockCount > compareBlockCount ? inputBlockCount : compareBlockCount;

                var deltas = new List<Delta>();
                bool isCopy, isWrite, isDelete;
                int source;
                IEnumerable<Delta> blockDeltas;
                var handledBlocks = new HashSet<int>(maxBlockCount);

                for (var i = 0; i < maxBlockCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    (isCopy, blockDeltas) = IsCopy(i, input, comparer, handledBlocks);
                    if (isCopy)
                    {
                        deltas.AddRange(blockDeltas);
                        continue;
                    }

                    // (isWrite, source, blockDeltas) = IsWrite(i, input, comparer);
                    // if (isWrite)
                    // {
                    //     deltas.AddRange(blockDeltas ?? Array.Empty<Delta>());
                    //     continue;
                    // }

                    // (isDelete, blockDeltas) = IsDelete(i, input, comparer);
                    // if (isDelete)
                    // {
                    //     deltas.AddRange(blockDeltas ?? Array.Empty<Delta>());
                    //     continue;
                    // }
                }

                return new ReadOnlyCollection<Delta>(deltas) as IReadOnlyCollection<Delta>;
            }).ConfigureAwait(false);
        }

        public ReadOnlyMemory<byte>? GetHash(int blockIndex)
        {
            var exists = _signatureMapping.TryGetValue(blockIndex, out var result);
            return exists ? result : null;
        }

        public int? GetBlockIndex(ReadOnlyMemory<byte> hash)
        {
            var exists = _signatureMapping.Reverse.TryGetValue(hash, out var result);
            return exists ? result : null;
        }

        private static (bool, IEnumerable<Delta>) IsCopy(int blockIndex, ISignatureMap input, ISignatureMap comparer, ISet<int> handled)
        {
            var deltas = new List<Delta>();
            var inputHash = input.GetHash(blockIndex);
            var comparerHash = comparer.GetHash(blockIndex);

            // Invalid Operation
            if (inputHash is null && comparerHash is null)
            {
                return (false, deltas);
            }

            // comparerMap is larger (Delete Operation)
            if (inputHash is null)
            {
                return (false, deltas);
            }

            // inputMap is larger
            if (comparerHash is null)
            {
                // Data was copied to another spot
                var existingBlockIndex = comparer.GetBlockIndex(inputHash.Value);
                if (existingBlockIndex is not null && !handled.Contains(blockIndex))
                {
                    deltas.Add(new Delta {
                        OperationType = DeltaOperationType.Copy,
                        TargetBlockIndex = blockIndex,
                        SourceBlockIndex = existingBlockIndex.Value
                    });
                    handled.Add(blockIndex);

                    return (true, deltas);
                }
            }

            // inputMap and comparerMap have the same hash
            if (inputHash.Value.Span.SequenceEqual(comparerHash!.Value.Span))
            {
                return (false, deltas);
            }

            var comparerBlock = comparer.GetBlockIndex(inputHash.Value);
            var inputBlock = input.GetBlockIndex(comparerHash.Value);

            // input contains new data (Write Operation)
            if (inputBlock is null && comparerBlock is null)
            {
                return (false, deltas);
            }

            // comparer contains inputHash
            if (comparerBlock is not null && !handled.Contains(blockIndex))
            {
                deltas.Add(new Delta {
                    OperationType = DeltaOperationType.Copy,
                    TargetBlockIndex = blockIndex,
                    SourceBlockIndex = comparerBlock.Value
                });
                handled.Add(blockIndex);
            }

            // input contains comparerHash
            if (inputBlock is not null && !handled.Contains(inputBlock.Value))
            {
                deltas.Add(new Delta {
                    OperationType = DeltaOperationType.Copy,
                    TargetBlockIndex = inputBlock.Value,
                    SourceBlockIndex = blockIndex
                });
                handled.Add(inputBlock.Value);
            }

            return (true, deltas);
        }

        // private static (bool, int, IEnumerable<Delta>) IsWrite(int blockIndex, ISignatureMapOld input, ISignatureMapOld comparer, ISet<int> handled)
        // {
        //     var deltas = new List<Delta>();
        //     return (false, 1, deltas);
        //     //return input.SequenceEqual(comparer);
        // }

        // private static (bool, IEnumerable<Delta>) IsDelete(int blockIndex, ISignatureMapOld input, ISignatureMapOld comparer, ISet<int> handled)
        // {
        //     var deltas = new List<Delta>();
        //     return (false, deltas);
        //     //return input.SequenceEqual(comparer);
        // }

        private static ReadOnlyMemory<byte> ComputeHash(ReadOnlySpan<byte> buffer)
        {
            var hash = SHA384.HashData(buffer);
            return new ReadOnlyMemory<byte>(hash);
        }
    }
}