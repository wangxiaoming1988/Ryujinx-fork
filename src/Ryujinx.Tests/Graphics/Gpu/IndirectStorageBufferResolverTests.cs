using NUnit.Framework;
using Ryujinx.Graphics.Gpu.Memory;
using System.Collections.Generic;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class IndirectStorageBufferResolverTests
    {
        [Test]
        public void ResolvesSeparateMappedTargetAndDeduplicatesPointersInsideIt()
        {
            ulong targetAddress = 0x33b2e0000;
            ulong targetSize = 0x100000;
            ulong[] pointers = [targetAddress, 0, targetAddress + 0x10, targetAddress];
            IndirectStorageBufferTarget[] targets = new IndirectStorageBufferTarget[4];

            int count = IndirectStorageBufferResolver.Resolve(
                pointers,
                targets,
                address => address >= targetAddress && address < targetAddress + targetSize
                    ? targetAddress + targetSize - address
                    : 0,
                16,
                out bool truncated);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(truncated, Is.False);
            Assert.That(targets[0].Address, Is.EqualTo(targetAddress));
            Assert.That(targets[0].Size, Is.EqualTo(targetSize));
        }

        [Test]
        public void AlignsTargetBaseAndIncludesLeadingBytes()
        {
            IndirectStorageBufferTarget[] targets = new IndirectStorageBufferTarget[1];

            int count = IndirectStorageBufferResolver.Resolve(
                [0x1003UL],
                targets,
                _ => 0x100,
                16,
                out bool truncated);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(truncated, Is.False);
            Assert.That(targets[0].Address, Is.EqualTo(0x1000));
            Assert.That(targets[0].Size, Is.EqualTo(0x103));
        }

        [Test]
        public void IgnoresUnmappedPointersAndReportsTargetOverflow()
        {
            ulong[] pointers = [0x1000, 0x2000, 0x3000, 0x4000, 0x5000, 0x6000];
            HashSet<ulong> mapped = [0x1000, 0x2000, 0x4000, 0x5000, 0x6000];
            IndirectStorageBufferTarget[] targets = new IndirectStorageBufferTarget[4];

            int count = IndirectStorageBufferResolver.Resolve(
                pointers,
                targets,
                address => mapped.Contains(address) ? 0x100UL : 0UL,
                16,
                out bool truncated);

            Assert.That(count, Is.EqualTo(4));
            Assert.That(truncated, Is.True);
            Assert.That(targets[0].Address, Is.EqualTo(0x1000));
            Assert.That(targets[1].Address, Is.EqualTo(0x2000));
            Assert.That(targets[2].Address, Is.EqualTo(0x4000));
            Assert.That(targets[3].Address, Is.EqualTo(0x5000));
        }
    }
}
