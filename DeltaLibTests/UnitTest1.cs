using DeltaLib;
using DeltaLib.Hashing;
using DeltaLib.Models;
using System;
using System.IO;
using System.Text.Json;
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
            var factory = new BinarySignatureMapFactory(hashing, checksum);

            using var inputStream = new FileStream(@"C:\Users\loligans\Downloads\Samples\DeltaLib\HxDChangelog.txt", FileMode.Open);
            var inputMapping = await factory
                .SetBufferSize(1024 * 1024 * 1)
                .SetBlockSize(64)
                .Create()
                .CreateMapAsync(inputStream)
                .ConfigureAwait(false);
            var hashCount = DateTime.Now;
            var hashCountTotal = hashCount - start;

            using var compareStream = new FileStream(@"C:\Users\loligans\Downloads\Samples\DeltaLib\HxDChangelogModified.txt", FileMode.Open);
            var deltas = await inputMapping
                .CreateDeltaAsync(compareStream)
                .ConfigureAwait(false);
            var total = DateTime.Now - start;
            var deltaCountTotal = total - hashCountTotal;

            //var jsonOptions = new JsonSerializerOptions();
            //jsonOptions.Converters.Add(new MemoryJsonConverter());
            //jsonOptions.WriteIndented = true;
            //var output = JsonSerializer.Serialize(inputMapping, jsonOptions);
            //File.WriteAllText(@"C:\Users\loligans\Downloads\Samples\DeltaLib\signatures.json", output);
        }
    }
}
