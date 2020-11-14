using System.Collections.Generic;
using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using System.Threading;
using DeltaLib.Models;
using DeltaLib.Hashing;
using System.Collections.Immutable;
using System.Buffers;
using System.Linq;

namespace DeltaLib
{
    public interface ISignatureMap
    {
        IReadOnlyDictionary<uint, HashSet<ReadOnlyMemory<byte>>> SignatureMappings { get; }
        /// <summary>
        /// Creates a two-way mapping between block index and block hash.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> cannot be null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="input"/> must be readable.</exception>
        public Task<ISignatureMap> CreateMapAsync(Stream input, CancellationToken cancellationToken = default);

        /// <summary>
        /// Calculates a collection of <see cref="Delta" />'s using the specified <paramref name="comparer"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="comparer"/> cannot be null.</exception>
        /// <exception cref="InvalidOperationException">A mapping must exist on the instance of <see cref="ISignatureMap"/></exception>
        public Task<IReadOnlyCollection<Delta>> CreateDeltaAsync(Stream input, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Calculates the blocks that have changed in a file
    /// </summary>
    public class BinarySignatureMap : ISignatureMap
    {
        private readonly IHashingAlgorithm _hashingAlgorithm;
        private readonly IRollingHashingAlgorithm _rollingChecksum;
        private readonly Dictionary<uint, HashSet<ReadOnlyMemory<byte>>> _signatureMappings;
        public IReadOnlyDictionary<uint, HashSet<ReadOnlyMemory<byte>>> SignatureMappings => _signatureMappings;

        public static readonly int BufferSize = 4 * 1024 * 1024;
        public static readonly int BlockSize = 2048;

        public BinarySignatureMap(
            IHashingAlgorithm hashingAlgorithm,
            IRollingHashingAlgorithm rollingChecksum,
            Dictionary<uint, HashSet<ReadOnlyMemory<byte>>> signatureMappings) : this(hashingAlgorithm, rollingChecksum)
        {
            if (signatureMappings is null) { throw new ArgumentNullException(nameof(signatureMappings)); }
            _signatureMappings = signatureMappings;
        }

        public BinarySignatureMap(
            IHashingAlgorithm hashingAlgorithm,
            IRollingHashingAlgorithm rollingChecksum)
        {
            if (hashingAlgorithm is null) { throw new ArgumentNullException(nameof(hashingAlgorithm)); }
            if (rollingChecksum is null) { throw new ArgumentNullException(nameof(rollingChecksum)); }

            _hashingAlgorithm = hashingAlgorithm;
            _rollingChecksum = rollingChecksum;
            _signatureMappings ??= new Dictionary<uint, HashSet<ReadOnlyMemory<byte>>>();
        }

        public async Task<ISignatureMap> CreateMapAsync(Stream input, CancellationToken cancellationToken = default)
        {
            if (input is null) { throw new ArgumentNullException(nameof(input)); }
            if (input.CanRead is false) { throw new InvalidOperationException($"{nameof(input)} cannot be read"); }

            var readerOptions = new StreamPipeReaderOptions(null, BufferSize, BlockSize);
            var inputReader = PipeReader.Create(input, readerOptions);

            // Start processing the pipe
            ReadResult result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);;
            ReadOnlySequence<byte> buffer;
            long bufferLength;
            ReadOnlySequence<byte> block;
            while (!result.IsCompleted)
            {
                buffer = result.Buffer;
                bufferLength = buffer.Length;
                // Process the buffered data
                long bufferIndex = 0;
                do
                {
                    // Change the block size when we reach the the end of the pipe
                    var blockSize = bufferIndex + BlockSize > bufferLength ? bufferLength - bufferIndex : BlockSize;

                    // Calculate the checksum and hash using a slice of the buffer
                    block = buffer.Slice(bufferIndex, blockSize);
                    var checksum = _rollingChecksum.Calculate(ref block, (int)blockSize);
                    var hash = _hashingAlgorithm.ComputeHash(ref block, (int)blockSize);

                    // Prevent checksum collisions
                    if (_signatureMappings.ContainsKey(checksum))
                    {
                        var hashSet = _signatureMappings[checksum];
                        hashSet.Add(hash);
                    }
                    else
                    {
                        _signatureMappings.Add(checksum, new HashSet<ReadOnlyMemory<byte>> { hash });
                    }

                    bufferIndex += blockSize;
                } while (bufferIndex < bufferLength);

                // Advance to the reader to the next block
                inputReader.AdvanceTo(result.Buffer.GetPosition(bufferIndex));
                result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            // Close the file
            await inputReader.CompleteAsync().ConfigureAwait(false);
            return this;
        }

        public async Task<IReadOnlyCollection<Delta>> CreateDeltaAsync(Stream input, CancellationToken cancellationToken = default)
        {
            if (input is null) { throw new ArgumentNullException(nameof(input)); }
            if (input.CanRead is false) { throw new InvalidOperationException($"{nameof(input)} cannot be read"); }
            if (input.CanSeek is false) { throw new InvalidOperationException($"{nameof(input)} cannot seek"); }

            var readerOptions = new StreamPipeReaderOptions(null, BufferSize, BlockSize);
            var inputReader = PipeReader.Create(input, readerOptions);
            var deltas = new List<Delta>();

            ReadResult result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer;
            long bufferLength;
            long bufferThreshold;
            long position = 0;
            long lastMatch;
            uint checksum = 0;
            // Process the buffer until there is nothing left
            while (result.Buffer.Length > 0)
            {
                buffer = result.Buffer;
                bufferLength = result.Buffer.Length;
                bufferThreshold = bufferLength < BlockSize ? bufferLength : bufferLength - BlockSize;
                bool recalculate = true;
                long bufferIndex = 0;
                long blockSize;
                ReadOnlySequence<byte> checksumBlock;
                long missingPosition = 0;
                while (bufferIndex < bufferThreshold)
                {
                    // Use the correct block size when we reach the the end of the PipeReader
                    blockSize = bufferIndex + BlockSize >= bufferLength
                        ? bufferLength - bufferIndex
                        : BlockSize;

                    // Update checksum
                    if (recalculate)
                    {
                        checksumBlock = buffer.Slice(bufferIndex, blockSize);
                        checksum = _rollingChecksum.Calculate(ref checksumBlock, (int)blockSize);
                        recalculate = false;
                    }
                    else
                    {
                        checksumBlock = buffer.Slice(bufferIndex - 1, blockSize + 1);
                        checksum = _rollingChecksum.Rotate(ref checksumBlock, checksum, (int)blockSize);
                    }

                    // This block exists in the _signatureMapping
                    if (_signatureMappings.ContainsKey(checksum))
                    {
                        // Validate Hash
                        var hashSet = _signatureMappings[checksum];

                        var block = buffer.Slice(bufferIndex, blockSize);
                        var blockHash = _hashingAlgorithm.ComputeHash(ref block, (int)blockSize);

                        var isEqual = hashSet.Contains(blockHash, HashEqualityComparer.Default);
                        if (isEqual)
                        {
                            // Insert Missing Data Previously
                            if (missingPosition > 0)
                            {
                                Console.WriteLine($"Missing data: {missingPosition} Bytes: {position - missingPosition}");
                            }

                            // Insert Matched Data
                            lastMatch = position;
                            position += blockSize;
                            deltas.Add(new Delta {
                                Checksum = checksum,
                                Target = lastMatch,
                                Length = position
                            });
                            recalculate = true;
                            bufferIndex += blockSize;
                            missingPosition = 0;
                            continue;
                        }
                    }
                    // The last block in the file does not match
                    else if (bufferThreshold == bufferLength)
                    {
                        Console.WriteLine($"Missing data: {position} Bytes: {bufferLength}");
                        bufferIndex += blockSize;
                        position += blockSize;
                        continue;
                    }

                    // Store the position of missing data
                    missingPosition = missingPosition > 0 ? missingPosition : position;
                    bufferIndex += 1;
                    position += 1;
                }

                // Advance the read to the next block
                inputReader.AdvanceTo(buffer.GetPosition(bufferIndex), buffer.End);
                result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            // Close the file
            await inputReader.CompleteAsync().ConfigureAwait(false);
            return deltas.ToImmutableList();
        }
    }
}