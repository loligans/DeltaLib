using System.Threading.Tasks;
using System;
using Xunit;
using DeltaLib;
using System.IO;
using System.Threading;
using DeltaLib.Collections;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace DeltaLibTests
{
    public class UnitTest1
    {
        [Fact]
        public async Task Test1()
        {
            // using var stream = new FileStream(@"C:\Users\loligans\Downloads\Samples\ndp48-devpack-enu.exe", FileMode.Open);
            var inputMapping = new BinarySignatureMap(
                new BidirectionalDictionary<int, ReadOnlyMemory<byte>>(EqualityComparer<int>.Default, HashEqualityComparer.Default) {
                    { 0, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x61 })) },
                    { 1, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x62 })) },
                    { 2, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x63 })) },
                    { 3, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x64 })) },
                    { 4, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x65 })) }
            });
            var compareMapping = new BinarySignatureMap(
                new BidirectionalDictionary<int, ReadOnlyMemory<byte>>(EqualityComparer<int>.Default, HashEqualityComparer.Default) {
                    { 0, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x61 })) },
                    { 1, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x63 })) },
                    { 2, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x62 })) },
                    { 3, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x66 })) },
                    { 4, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x6b })) },
                    { 5, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x65 })) },
                    { 6, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x68 })) },
                    { 7, new ReadOnlyMemory<byte>(SHA384.HashData(new byte[] { 0x6c })) }
            });

            await inputMapping.CreateDelta(compareMapping).ConfigureAwait(false);
        }
    }
}
