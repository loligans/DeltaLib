using DeltaLib;
using DeltaLib.Hashing;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace DeltaLibTests
{
    public class UnitTest1
    {
        [Fact]
        public async Task Test1()
        {
            var start = DateTime.Now;
            var checksum = new DeltaLib.Hashing.Adler32HashingAlgorithm();
            var hashing = new SHA384HashingAlgorithm();

            using var inputStream = new FileStream(@"C:\Users\loligans\Downloads\Samples\DeltaLib\ndp48-devpack-enu.exe", FileMode.Open);
            var inputMapping = await new BinarySignatureMap(hashing, checksum)
                .CreateMapAsync(inputStream, default)
                .ConfigureAwait(false);
            var hashCount = DateTime.Now;

            using var compareStream = new FileStream(@"C:\Users\loligans\Downloads\Samples\DeltaLib\ndp48-devpack-enu - Copy.exe", FileMode.Open);
            var result = await inputMapping.CreateDeltaAsync(compareStream);
            var total = DateTime.Now - start;
            var hashCountTotal = hashCount - start;
            var deltaCountTotal = total - hashCountTotal;
        }
    }
}
