using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Gpu.Memory;
using Ryujinx.Graphics.Shader;
using Ryujinx.Memory.Range;
using System;
using System.Diagnostics;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class BufferTextureBindingQueueTests
    {
        private static readonly FormatInfo R8 = new(Format.R8Unorm, 1, 1, 1, 1);

        [Test]
        public void RepeatedSingleBindingIsUpsertedByPipelineDestination()
        {
            BufferTextureBindingQueue queue = new();
            ITexture texture = new TestTexture();
            TextureBindingInfo bindingInfo = CreateBinding(Target.TextureBuffer, binding: 3);

            for (int index = 0; index < 10_000; index++)
            {
                queue.Enqueue(new BufferTextureBinding(
                    ShaderStage.Fragment,
                    texture,
                    new MultiRange(0x1000 + (ulong)index, 16),
                    bindingInfo,
                    isImage: false));
            }

            Assert.That(queue.TextureCount, Is.EqualTo(1));
            Assert.That(queue.Textures[0].Range.GetSubRange(0).Address, Is.EqualTo(0x1000UL + 9_999));
        }

        [Test]
        public void DifferentSingleBindingDestinationsRemainIndependent()
        {
            BufferTextureBindingQueue queue = new();
            ITexture texture = new TestTexture();

            queue.Enqueue(new BufferTextureBinding(
                ShaderStage.Vertex,
                texture,
                new MultiRange(0x1000, 16),
                CreateBinding(Target.TextureBuffer, binding: 3),
                isImage: false));
            queue.Enqueue(new BufferTextureBinding(
                ShaderStage.Fragment,
                texture,
                new MultiRange(0x2000, 16),
                CreateBinding(Target.TextureBuffer, binding: 3),
                isImage: false));
            queue.Enqueue(new BufferTextureBinding(
                ShaderStage.Fragment,
                texture,
                new MultiRange(0x3000, 16),
                CreateBinding(Target.TextureBuffer, binding: 3),
                isImage: true));

            Assert.That(queue.TextureCount, Is.EqualTo(3));
        }

        [Test]
        public void ArrayBindingIsUpsertedByArrayIdentityAndIndex()
        {
            BufferTextureBindingQueue queue = new();
            ITexture texture = new TestTexture();
            ITextureArray firstArray = new TestTextureArray();
            ITextureArray secondArray = new TestTextureArray();
            TextureBindingInfo bindingInfo = CreateBinding(Target.TextureBuffer, binding: 4);

            queue.Enqueue(new BufferTextureArrayBinding<ITextureArray>(
                ShaderStage.Fragment,
                firstArray,
                texture,
                new MultiRange(0x1000, 16),
                bindingInfo,
                index: 0));
            queue.Enqueue(new BufferTextureArrayBinding<ITextureArray>(
                ShaderStage.Fragment,
                firstArray,
                texture,
                new MultiRange(0x2000, 16),
                bindingInfo,
                index: 0));
            queue.Enqueue(new BufferTextureArrayBinding<ITextureArray>(
                ShaderStage.Fragment,
                firstArray,
                texture,
                new MultiRange(0x3000, 16),
                bindingInfo,
                index: 1));
            queue.Enqueue(new BufferTextureArrayBinding<ITextureArray>(
                ShaderStage.Fragment,
                secondArray,
                texture,
                new MultiRange(0x4000, 16),
                bindingInfo,
                index: 0));

            Assert.That(queue.TextureArrayCount, Is.EqualTo(3));
            Assert.That(queue.TextureArrays[0].Range.GetSubRange(0).Address, Is.EqualTo(0x2000UL));
        }

        [Test]
        public void ImageArrayBindingHasItsOwnQueueAndCommitClearsAllEntries()
        {
            BufferTextureBindingQueue queue = new();
            ITexture texture = new TestTexture();
            IImageArray imageArray = new TestImageArray();

            queue.Enqueue(new BufferTextureArrayBinding<IImageArray>(
                ShaderStage.Compute,
                imageArray,
                texture,
                new MultiRange(0x5000, 16),
                CreateBinding(Target.TextureBuffer, binding: 5, flags: TextureUsageFlags.ImageStore),
                index: 2));

            Assert.That(queue.ImageArrayCount, Is.EqualTo(1));

            queue.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(queue.TextureCount, Is.Zero);
                Assert.That(queue.TextureArrayCount, Is.Zero);
                Assert.That(queue.ImageArrayCount, Is.Zero);
            });
        }

        [Test]
        public void WarmedRepeatedUpsertsDoNotAllocateUnboundedMemory()
        {
            BufferTextureBindingQueue queue = new();
            ITexture texture = new TestTexture();
            TextureBindingInfo bindingInfo = CreateBinding(Target.TextureBuffer, binding: 7);
            BufferTextureBinding binding = new(
                ShaderStage.Fragment,
                texture,
                new MultiRange(0x1000, 16),
                bindingInfo,
                isImage: false);

            queue.Enqueue(binding);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < 10_000; index++)
            {
                queue.Enqueue(binding);
            }

            stopwatch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(queue.TextureCount, Is.EqualTo(1));
            Assert.That(allocated, Is.LessThanOrEqualTo(4 * 1024));
            TestContext.Progress.WriteLine(
                $"Buffer binding queue: 10,000 upserts in {stopwatch.Elapsed.TotalMilliseconds:F3} ms, " +
                $"peak entries={queue.TextureCount}, managed allocations={allocated} bytes.");
        }

        private static TextureBindingInfo CreateBinding(Target target, int binding, TextureUsageFlags flags = TextureUsageFlags.None)
        {
            return new TextureBindingInfo(
                target,
                R8,
                set: 2,
                binding,
                arrayLength: 1,
                cbufSlot: 0,
                handle: 0,
                flags);
        }

        private sealed class TestTexture : ITexture
        {
            public int Width => 1;
            public int Height => 1;

            public void CopyTo(ITexture destination, int firstLayer, int firstLevel) { }
            public void CopyTo(ITexture destination, int srcLayer, int dstLayer, int srcLevel, int dstLevel) { }
            public void CopyTo(ITexture destination, Extents2D srcRegion, Extents2D dstRegion, bool linearFilter) { }
            public void CopyTo(BufferRange range, int layer, int level, int stride) { }
            public ITexture CreateView(TextureCreateInfo info, int firstLayer, int firstLevel) => this;
            public PinnedSpan<byte> GetData() => PinnedSpan<byte>.UnsafeFromSpan(ReadOnlySpan<byte>.Empty);
            public PinnedSpan<byte> GetData(int layer, int level) => GetData();
            public void SetData(Ryujinx.Common.Memory.MemoryOwner<byte> data) => data.Dispose();
            public void SetData(Ryujinx.Common.Memory.MemoryOwner<byte> data, int layer, int level) => data.Dispose();
            public void SetData(Ryujinx.Common.Memory.MemoryOwner<byte> data, int layer, int level, Rectangle<int> region) => data.Dispose();
            public void SetStorage(BufferRange buffer) { }
            public void Release() { }
        }

        private sealed class TestTextureArray : ITextureArray
        {
            public void SetSamplers(int index, ISampler[] samplers) { }
            public void SetTextures(int index, ITexture[] textures) { }
            public void Dispose() { }
        }

        private sealed class TestImageArray : IImageArray
        {
            public void SetImages(int index, ITexture[] images) { }
            public void Dispose() { }
        }
    }
}
