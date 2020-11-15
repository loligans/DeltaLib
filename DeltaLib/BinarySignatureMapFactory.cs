using DeltaLib.Hashing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeltaLib
{
    public interface IBinarySignatureMapFactory
    {
        IBinarySignatureMapFactory SetBufferSize(int bufferSize);
        IBinarySignatureMapFactory SetBlockSize(int blockSize);
        ISignatureMap Create();
    }
    public class BinarySignatureMapFactory : IBinarySignatureMapFactory
    {
        private IHashingAlgorithm? _hashingAlgorithm;
        private IRollingHashingAlgorithm? _rollingHashingAlgorithm;
        private int _bufferSize = 4 * 1024 * 1024;
        private int _blockSize = 2048;

        public BinarySignatureMapFactory() { }
        public BinarySignatureMapFactory(
            IHashingAlgorithm hashingAlgorithm, 
            IRollingHashingAlgorithm rollingHashingAlgorithm)
        {
            _hashingAlgorithm = hashingAlgorithm;
            _rollingHashingAlgorithm = rollingHashingAlgorithm;
        }

        public ISignatureMap Create()
        {
            if (_hashingAlgorithm is null) { throw new ArgumentNullException(nameof(_hashingAlgorithm)); }
            if (_rollingHashingAlgorithm is null) { throw new ArgumentNullException(nameof(_rollingHashingAlgorithm)); }
            if (_bufferSize <= _blockSize) { throw new ArgumentException("Buffer size can't be smaller than block size."); }
            if (_bufferSize <= 0 || _blockSize <= 0) { throw new ArgumentException("Both buffer size and block size cannot be negative."); }

            return new BinarySignatureMap(
                _hashingAlgorithm, 
                _rollingHashingAlgorithm)
                {
                    BufferSize = _bufferSize,
                    BlockSize = _blockSize
                };
        }

        public IBinarySignatureMapFactory SetBlockSize(int blockSize)
        {
            _blockSize = blockSize;
            return this;
        }

        public IBinarySignatureMapFactory SetBufferSize(int bufferSize)
        {
            _bufferSize = bufferSize;
            return this;
        }
    }
}
