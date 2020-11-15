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
        IReadOnlyDictionary<uint, HashSet<Signature>> SignatureMappings { get; }
        /// <summary>
        /// Creates a map for the specified <paramref name="input"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="input"/> is unreadable.</exception>
        public Task<ISignatureMap> CreateMapAsync(Stream input, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates deltas for the specified <paramref name="input"/>
        /// </summary>
        /// <exception cref="ArgumentNullException">Either <paramref name="input"/> or <see cref="SignatureMappings"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="input"/> is unreadable./></exception>
        public Task<IReadOnlyCollection<Delta>> CreateDeltaAsync(Stream input, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Calculates the blocks that have changed in a file
    /// </summary>
    public class BinarySignatureMap : ISignatureMap
    {
        private readonly IHashingAlgorithm _hashingAlgorithm;
        private readonly IRollingHashingAlgorithm _rollingChecksum;
        private readonly Dictionary<uint, HashSet<Signature>> _signatureMappings;
        public IReadOnlyDictionary<uint, HashSet<Signature>> SignatureMappings => _signatureMappings;

        public int BufferSize { get; init; }
        public int BlockSize { get; init; }

        public BinarySignatureMap(
            IHashingAlgorithm hashingAlgorithm,
            IRollingHashingAlgorithm rollingChecksum,
            Dictionary<uint, HashSet<Signature>> signatureMappings) : this(hashingAlgorithm, rollingChecksum)
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
            _signatureMappings ??= new Dictionary<uint, HashSet<Signature>>(EqualityComparer<uint>.Default);
        }

        public async Task<ISignatureMap> CreateMapAsync(Stream input, CancellationToken cancellationToken = default)
        {
            if (input is null) { throw new ArgumentNullException(nameof(input)); }
            if (input.CanRead is false) { throw new InvalidOperationException($"{nameof(input)} cannot be read"); }

            var readerOptions = new StreamPipeReaderOptions(null, BufferSize, BlockSize);
            var inputReader = PipeReader.Create(input, readerOptions);

            // Start processing the pipe
            long position = 0;
            ReadResult result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);;
            ReadOnlySequence<byte> buffer;
            long bufferLength;
            ReadOnlySequence<byte> block;
            Signature signature;
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
                    signature = new()
                    {
                        Hash = hash,
                        Checksum = checksum,
                        Target = position,
                        Length = blockSize
                    };

                    // Prevent checksum collisions
                    if (_signatureMappings.ContainsKey(checksum))
                    {
                        var hashSet = _signatureMappings[checksum];
                        hashSet.Add(signature);
                    }
                    else
                    {
                        _signatureMappings.Add(checksum, new HashSet<Signature>(SignatureEqualityComparer.Default) { signature });
                    }

                    bufferIndex += blockSize;
                    position += blockSize;
                } while (bufferIndex < bufferLength);

                // Advance to the reader to the next block
                inputReader.AdvanceTo(buffer.GetPosition(bufferIndex));
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

            var readerOptions = new StreamPipeReaderOptions(null, BufferSize, BlockSize);
            var inputReader = PipeReader.Create(input, readerOptions);
            var deltas = new List<Delta>();

            // Grab the bufferLength
            ReadResult result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            long bufferLength = buffer.Length;

            Signature signature;
            Delta delta;
            long bufferThreshold;
            long position = 0;
            uint checksum = 0;
            long missingPosition = -1;

            // Process the buffer until there is nothing left
            while (bufferLength > 0)
            {
                // Update the buffer on each read
                buffer = result.Buffer;
                bufferLength = result.Buffer.Length;

                bool recalculate = true;
                long bufferIndex = 0;
                long blockSize;
                ReadOnlySequence<byte> checksumBlock;

                // Give a little room in the buffer so that it can transition into the next block
                bufferThreshold = bufferLength <= BlockSize ? bufferLength : bufferLength - BlockSize;
                while (bufferIndex < bufferThreshold)
                {
                    // Ensure the correct block size is used when the buffer is at the end of the PipeReader
                    blockSize = bufferIndex + BlockSize >= bufferLength
                        ? bufferLength - bufferIndex
                        : BlockSize;

                    // Update the checksum
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

                    // Validate the checksum exists in _signatureMapping
                    if (_signatureMappings.ContainsKey(checksum))
                    {
                        // Calculate the buffer's hash value
                        var block = buffer.Slice(bufferIndex, blockSize);
                        var blockHash = _hashingAlgorithm.ComputeHash(ref block, (int)blockSize);
                        signature = new() { Hash = blockHash, Checksum = checksum };

                        // Validate the hash values match
                        var hashSet = _signatureMappings[checksum];
                        var isEqual = hashSet.Contains(signature);
                        if (isEqual)
                        {
                            hashSet.TryGetValue(signature, out signature!);

                            // Insert Missing Data Previously
                            if (missingPosition >= 0)
                            {
                                Console.WriteLine($"Missing data: {missingPosition} Bytes: {position - missingPosition}");
                                var missing = position - missingPosition;
                                missing = missing < blockSize ? blockSize : missing;
                                var buff = buffer.Slice(missingPosition, missing);
                                deltas.Add(new()
                                {
                                    OperationType = DeltaOperationType.Write,
                                    Data = buff.First,
                                    Hash = _hashingAlgorithm.ComputeHash(ref buff, (int)missing),
                                    Checksum = _rollingChecksum.Calculate(ref buff, (int)missing),
                                    Target = missingPosition,
                                    Length = signature.Target - missingPosition
                                });
                                missingPosition = -1;
                            }
                            // Nothing changed
                            if (signature.Target == position)
                            {
                                position += blockSize;
                                recalculate = true;
                                bufferIndex += blockSize;
                                continue;
                            }

                            // Data moved somewhere else
                            //deltas.Add(new()
                            //{
                            //    OperationType = DeltaOperationType.Copy,
                            //    Data = null,
                            //    Hash = signature.Hash,
                            //    Checksum = signature.Checksum,
                            //    Source = signature.Target,
                            //    Target = position,
                            //    Length = blockSize
                            //});
                            position += blockSize;
                            recalculate = true;
                            bufferIndex += blockSize;
                            continue;
                        }
                    }
                    // Special Case: The last block in the file does not match
                    else if (bufferThreshold == bufferLength)
                    {
                        Console.WriteLine($"Missing data: {position} Bytes: {bufferLength}");
                        missingPosition = bufferIndex;
                        bufferIndex += blockSize;
                        continue;
                    }

                    // Store the position of missing data
                    missingPosition = missingPosition > -1 ? missingPosition : bufferIndex;
                    bufferIndex += 1;
                    position += 1;
                }

                // Insert Missing Data Previously
                if (missingPosition >= 0)
                {
                    Console.WriteLine($"Missing data: {missingPosition} Bytes: {position - missingPosition}");
                    var length = bufferIndex - missingPosition;
                    var buff = buffer.Slice(missingPosition, length);
                    deltas.Add(new()
                    {

                        OperationType = DeltaOperationType.Delete,
                        Data = buff.First,
                        Hash = _hashingAlgorithm.ComputeHash(ref buff, (int)length),
                        Checksum = _rollingChecksum.Calculate(ref buff, (int)length),
                        Target = position - missingPosition,
                        Length = length
                    });
                    missingPosition = -1;
                }

                // Advance the inputReader to the next block
                inputReader.AdvanceTo(buffer.GetPosition(bufferIndex), buffer.End);
                result = await inputReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            // Close the file
            await inputReader.CompleteAsync().ConfigureAwait(false);
            return deltas.ToImmutableList();
        }
    }
}