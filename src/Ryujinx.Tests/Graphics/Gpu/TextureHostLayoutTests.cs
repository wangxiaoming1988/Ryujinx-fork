using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class TextureHostLayoutTests
    {
        [Test]
        public void OversizedLinearR8TexturePagedLayoutUsesTwoPages()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x2ea930000,
                width: 1024,
                height: 32768,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Capabilities caps = CreateVulkanCapabilities();

            Assert.That(
                TextureHostLayout.TryGetPagedLinear2D(info, caps, formatInfo, 1f, out TextureHostLayout layout),
                Is.True);
            Assert.That(layout.Width, Is.EqualTo(1024));
            Assert.That(layout.Height, Is.EqualTo(32768));
            Assert.That(layout.Stride, Is.EqualTo(1024));
            Assert.That(layout.TexelCount, Is.EqualTo(33554432));

            Assert.That(
                () => TextureCache.GetCreateInfo(
                    info,
                    caps,
                    1f,
                    blockUnsafeMacOSPagedTextures: true),
                Throws.TypeOf<MacOSGpuSafetyException>());

            TextureCreateInfo createInfo = TextureCache.GetCreateInfo(
                info,
                caps,
                1f,
                blockUnsafeMacOSPagedTextures: false);

            Assert.That(createInfo.Target, Is.EqualTo(Target.Texture2DArray));
            Assert.That(createInfo.Width, Is.EqualTo(1024));
            Assert.That(createInfo.Height, Is.EqualTo(TextureHostLayout.MetalMaxTexture2DDimension));
            Assert.That(createInfo.Depth, Is.EqualTo(2));

            Assert.That(layout.IsPaged, Is.True);
        }

        [TestCase(true, null, true)]
        [TestCase(true, "", true)]
        [TestCase(true, "true", true)]
        [TestCase(true, "1", false)]
        [TestCase(false, null, false)]
        public void MacOSGpuSafetyPolicyRequiresExplicitUnsafeOverride(
            bool isMacOS,
            string unsafeOverride,
            bool expectedBlocked)
        {
            Assert.That(
                MacOSGpuSafetyPolicy.ShouldBlockUnsafePagedTextures(isMacOS, unsafeOverride),
                Is.EqualTo(expectedBlocked));
        }

        [Test]
        public void OversizedLinearR8TextureUsesPagedHostLayout()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x2ea930000,
                width: 1024,
                height: 32768,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Assert.That(
                TextureHostLayout.TryGetPagedLinear2D(info, CreateVulkanCapabilities(), formatInfo, 1f, out TextureHostLayout layout),
                Is.True);
            Assert.That(layout.PageHeight, Is.EqualTo(TextureHostLayout.MetalMaxTexture2DDimension));
            Assert.That(layout.PageCount, Is.EqualTo(2));
            Assert.That(layout.TexelCount, Is.EqualTo(33554432));
        }

        [Test]
        public void NonOversizedLinearR8TextureKeepsNormalTexturePath()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x1000,
                width: 1024,
                height: TextureHostLayout.MetalMaxTexture2DDimension,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Assert.That(TextureHostLayout.TryGetBufferBackedLinear2D(info, CreateVulkanCapabilities(), formatInfo, 1f, out _), Is.False);
        }

        [Test]
        public void OversizedLinearR8DescriptorEntersBufferBackedShaderState()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            TextureDescriptor descriptor = CreateLinearR8Descriptor(1024, 32768, 1024);

            Assert.That(
                TextureHostLayout.GetBufferBackedLinear2DState(descriptor, isVulkan: true, bufferBackedEnabled: true),
                Is.EqualTo(8193));
        }

        [Test]
        public void OversizedLinearR8DescriptorEntersPagedShaderState()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            TextureDescriptor descriptor = CreateLinearR8Descriptor(1024, 32768, 1024);
            int state = TextureHostLayout.GetPagedLinear2DState(descriptor, isVulkan: true);

            Assert.That(TextureHostLayout.IsPagedLinear2DState(state), Is.True);
            Assert.That(state, Is.GreaterThan(0));
        }

        [Test]
        public void PagedLayoutRejectsNonPageAlignedHeight()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x1000,
                width: 1024,
                height: TextureHostLayout.MetalMaxTexture2DDimension + 1,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Assert.That(TextureHostLayout.TryGetPagedLinear2D(info, CreateVulkanCapabilities(), formatInfo, 1f, out _), Is.False);
        }

        [Test]
        public void BufferBackedShaderStateRejectsInvalidStrideAndFormat()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            TextureDescriptor invalidStride = CreateLinearR8Descriptor(1024, 32768, 512);
            TextureDescriptor invalidFormat = CreateLinearR8Descriptor(1024, 32768, 1024);
            invalidFormat.Word0 = 0x2491b;

            Assert.Multiple(() =>
            {
                Assert.That(TextureHostLayout.GetBufferBackedLinear2DState(invalidStride, isVulkan: true, bufferBackedEnabled: true), Is.Zero);
                Assert.That(TextureHostLayout.GetBufferBackedLinear2DState(invalidFormat, isVulkan: true, bufferBackedEnabled: true), Is.Zero);
            });
        }

        [Test]
        public void OversizedLinearR8BufferPathCanBeDisabledBySafetyPolicy()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x2ea930000,
                width: 1024,
                height: 32768,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Assert.That(
                TextureHostLayout.TryGetBufferBackedLinear2D(
                    info,
                    CreateVulkanCapabilities(),
                    formatInfo,
                    1f,
                    bufferBackedEnabled: false,
                    out _),
                Is.False);

            TextureDescriptor descriptor = CreateLinearR8Descriptor(1024, 32768, 1024);

            Assert.That(
                TextureHostLayout.GetBufferBackedLinear2DState(
                    descriptor,
                    isVulkan: true,
                    bufferBackedEnabled: false),
                Is.Zero);
        }

        [Test]
        public void OversizedLinearR8BufferPathIsDisabledAtRuntime()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x2ea930000,
                width: 1024,
                height: 32768,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            TextureDescriptor descriptor = CreateLinearR8Descriptor(1024, 32768, 1024);

            Assert.Multiple(() =>
            {
                Assert.That(
                    TextureHostLayout.TryGetBufferBackedLinear2D(
                        info,
                        CreateVulkanCapabilities(),
                        formatInfo,
                        1f,
                        out _),
                    Is.False);
                Assert.That(
                    TextureHostLayout.GetBufferBackedLinear2DState(descriptor, isVulkan: true),
                    Is.Zero);
            });
        }

        [Test]
        public void UnsupportedOversizedTextureIsNotSilentlyClamped()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Ignore("Metal oversized linear texture fallback is macOS-specific.");
            }

            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x1000,
                width: 1024,
                height: TextureHostLayout.MetalMaxTexture2DDimension + 1,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            TextureCreateInfo createInfo = TextureCache.GetCreateInfo(info, CreateVulkanCapabilities(), 1f);

            Assert.That(createInfo.Height, Is.EqualTo(TextureHostLayout.MetalMaxTexture2DDimension + 1));
        }

        [Test]
        public void PagedRegionInSecondPageMapsToArrayLayerOne()
        {
            TextureHostLayout layout = CreatePagedLayout();
            Rectangle<int> region = new(64, 20000, 256, 32);

            PagedTextureRegion[] regions = layout.GetPagedRegions(region, 256 * 32, bytesPerPixel: 1);

            Assert.That(regions, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].Layer, Is.EqualTo(1));
                Assert.That(regions[0].Region, Is.EqualTo(new Rectangle<int>(64, 3616, 256, 32)));
                Assert.That(regions[0].SourceOffset, Is.Zero);
                Assert.That(regions[0].SourceLength, Is.EqualTo(8192));
            });
        }

        [Test]
        public void PagedRegionCrossingBoundarySplitsRowsWithoutOverlap()
        {
            TextureHostLayout layout = CreatePagedLayout();
            Rectangle<int> region = new(0, 16380, 1024, 8);

            PagedTextureRegion[] regions = layout.GetPagedRegions(region, 1024 * 8, bytesPerPixel: 1);

            Assert.That(regions, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].Layer, Is.Zero);
                Assert.That(regions[0].Region, Is.EqualTo(new Rectangle<int>(0, 16380, 1024, 4)));
                Assert.That(regions[0].SourceOffset, Is.Zero);
                Assert.That(regions[0].SourceLength, Is.EqualTo(4096));
                Assert.That(regions[1].Layer, Is.EqualTo(1));
                Assert.That(regions[1].Region, Is.EqualTo(new Rectangle<int>(0, 0, 1024, 4)));
                Assert.That(regions[1].SourceOffset, Is.EqualTo(4096));
                Assert.That(regions[1].SourceLength, Is.EqualTo(4096));
            });
        }

        [Test]
        public void PagedFullSliceMapsEveryPage()
        {
            TextureHostLayout layout = CreatePagedLayout();

            PagedTextureRegion[] regions = layout.GetPagedRegions(
                new Rectangle<int>(0, 0, 1024, 32768),
                1024 * 32768,
                bytesPerPixel: 1);

            Assert.That(regions, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].Layer, Is.Zero);
                Assert.That(regions[0].SourceLength, Is.EqualTo(1024 * 16384));
                Assert.That(regions[1].Layer, Is.EqualTo(1));
                Assert.That(regions[1].SourceOffset, Is.EqualTo(1024 * 16384));
                Assert.That(regions[1].SourceLength, Is.EqualTo(1024 * 16384));
            });
        }

        [Test]
        public void PagedCopyInSecondPageUsesLocalHostCoordinates()
        {
            TextureHostLayout layout = CreatePagedLayout();

            PagedTextureCopyRegion[] regions = TextureHostLayout.GetPagedCopyRegions(
                layout,
                null,
                new Extents2D(16, 20000, 528, 20128),
                new Extents2D(32, 400, 1056, 656));

            Assert.That(regions, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].SourceLayer, Is.EqualTo(1));
                Assert.That(regions[0].DestinationLayer, Is.Zero);
                Assert.That(regions[0].SourceRegion, Is.EqualTo(new Extents2D(16, 3616, 528, 3744)));
                Assert.That(regions[0].DestinationRegion, Is.EqualTo(new Extents2D(32, 400, 1056, 656)));
            });
        }

        [Test]
        public void PagedCopyCrossingSourceBoundarySplitsDestinationProportionally()
        {
            TextureHostLayout layout = CreatePagedLayout();

            PagedTextureCopyRegion[] regions = TextureHostLayout.GetPagedCopyRegions(
                layout,
                null,
                new Extents2D(0, 16000, 1024, 17000),
                new Extents2D(0, 0, 512, 500));

            Assert.That(regions, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].SourceLayer, Is.Zero);
                Assert.That(regions[0].SourceRegion, Is.EqualTo(new Extents2D(0, 16000, 1024, 16384)));
                Assert.That(regions[0].DestinationRegion, Is.EqualTo(new Extents2D(0, 0, 512, 192)));

                Assert.That(regions[1].SourceLayer, Is.EqualTo(1));
                Assert.That(regions[1].SourceRegion, Is.EqualTo(new Extents2D(0, 0, 1024, 616)));
                Assert.That(regions[1].DestinationRegion, Is.EqualTo(new Extents2D(0, 192, 512, 500)));
            });
        }

        [Test]
        public void PagedCopyCrossingBothLayoutsUsesMatchingArrayLayers()
        {
            TextureHostLayout layout = CreatePagedLayout();

            PagedTextureCopyRegion[] regions = TextureHostLayout.GetPagedCopyRegions(
                layout,
                layout,
                new Extents2D(0, 16380, 1024, 16388),
                new Extents2D(0, 16380, 1024, 16388));

            Assert.That(regions, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(regions[0].SourceLayer, Is.Zero);
                Assert.That(regions[0].DestinationLayer, Is.Zero);
                Assert.That(regions[0].SourceRegion, Is.EqualTo(new Extents2D(0, 16380, 1024, 16384)));
                Assert.That(regions[0].DestinationRegion, Is.EqualTo(new Extents2D(0, 16380, 1024, 16384)));

                Assert.That(regions[1].SourceLayer, Is.EqualTo(1));
                Assert.That(regions[1].DestinationLayer, Is.EqualTo(1));
                Assert.That(regions[1].SourceRegion, Is.EqualTo(new Extents2D(0, 0, 1024, 4)));
                Assert.That(regions[1].DestinationRegion, Is.EqualTo(new Extents2D(0, 0, 1024, 4)));
            });
        }

        [Test]
        public void PagedCopyRejectsScaleThatCollapsesAPageSegment()
        {
            TextureHostLayout layout = CreatePagedLayout();

            Assert.Throws<InvalidOperationException>(() => TextureHostLayout.GetPagedCopyRegions(
                layout,
                null,
                new Extents2D(0, 0, 1024, 32768),
                new Extents2D(0, 0, 1024, 1)));
        }

        [Test]
        public void PagedCopyRejectsGuestRegionOutsideLayout()
        {
            TextureHostLayout layout = CreatePagedLayout();

            Assert.Throws<ArgumentOutOfRangeException>(() => TextureHostLayout.GetPagedCopyRegions(
                layout,
                null,
                new Extents2D(0, 0, 1024, 32769),
                new Extents2D(0, 0, 1024, 32769)));
        }

        private static TextureHostLayout CreatePagedLayout()
        {
            FormatInfo formatInfo = new(Format.R8Unorm, 1, 1, 1, 1);
            TextureInfo info = new(
                gpuAddress: 0x2ea930000,
                width: 1024,
                height: 32768,
                depthOrLayers: 1,
                levels: 1,
                samplesInX: 1,
                samplesInY: 1,
                stride: 1024,
                isLinear: true,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            Assert.That(
                TextureHostLayout.TryGetPagedLinear2D(info, CreateVulkanCapabilities(), formatInfo, 1f, out TextureHostLayout layout),
                Is.True);

            return layout;
        }

        [Test]
        public void InvalidTextureDimensionsAreNormalizedForHostImageCreation()
        {
            FormatInfo formatInfo = new(Format.R8G8B8A8Unorm, 1, 1, 4, 4);
            TextureInfo info = new(
                gpuAddress: 0x4000,
                width: 0,
                height: 0,
                depthOrLayers: 0,
                levels: 10,
                samplesInX: 0,
                samplesInY: 0,
                stride: 0,
                isLinear: false,
                gobBlocksInY: 1,
                gobBlocksInZ: 1,
                gobBlocksInTileX: 1,
                target: Target.Texture2D,
                formatInfo);

            TextureCreateInfo createInfo = TextureCache.GetCreateInfo(info, CreateVulkanCapabilities(), 1f);

            Assert.Multiple(() =>
            {
                Assert.That(createInfo.Width, Is.EqualTo(1));
                Assert.That(createInfo.Height, Is.EqualTo(1));
                Assert.That(createInfo.Depth, Is.EqualTo(1));
                Assert.That(createInfo.Levels, Is.EqualTo(1));
                Assert.That(createInfo.Samples, Is.EqualTo(1));
            });
        }

        [Test]
        public void DirectGalTextureDescriptorsCannotBeZeroSized()
        {
            TextureCreateInfo createInfo = new(
                width: 0,
                height: 0,
                depth: 0,
                levels: 0,
                samples: 0,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 4,
                format: Format.R8G8B8A8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target: Target.Texture2D,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);

            TextureCreateInfo validMipChain = new(
                width: 640,
                height: 360,
                depth: 1,
                levels: 10,
                samples: 1,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 4,
                format: Format.R8G8B8A8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target: Target.Texture2D,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);

            TextureCreateInfo invalidMipChain = new(
                width: 1,
                height: 1,
                depth: 1,
                levels: 10,
                samples: 1,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 4,
                format: Format.R8G8B8A8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target: Target.Texture2D,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);

            Assert.Multiple(() =>
            {
                Assert.That(createInfo.Width, Is.EqualTo(1));
                Assert.That(createInfo.Height, Is.EqualTo(1));
                Assert.That(createInfo.Depth, Is.EqualTo(1));
                Assert.That(createInfo.Levels, Is.EqualTo(1));
                Assert.That(createInfo.Samples, Is.EqualTo(1));
                Assert.That(validMipChain.Levels, Is.EqualTo(10));
                Assert.That(invalidMipChain.Levels, Is.EqualTo(1));
            });
        }

        [Test]
        public void MipmapLimitUsesTextureTargetAndSampleCount()
        {
            TextureCreateInfo volume = new(
                width: 1,
                height: 1,
                depth: 8,
                levels: 10,
                samples: 1,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 4,
                format: Format.R8G8B8A8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target: Target.Texture3D,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);

            TextureCreateInfo multisample = new(
                width: 64,
                height: 64,
                depth: 1,
                levels: 10,
                samples: 4,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 4,
                format: Format.R8G8B8A8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target: Target.Texture2DMultisample,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);

            Assert.Multiple(() =>
            {
                Assert.That(volume.Levels, Is.EqualTo(4));
                Assert.That(multisample.Levels, Is.EqualTo(1));
            });
        }

        private static TextureDescriptor CreateLinearR8Descriptor(int width, int height, int stride)
        {
            return new TextureDescriptor
            {
                Word0 = 0x2491d,
                Word2 = 2u << 21,
                Word3 = (uint)(stride / 32),
                Word4 = (uint)(width - 1) | (1u << 23),
                Word5 = (uint)(height - 1),
            };
        }

        private static Capabilities CreateVulkanCapabilities()
        {
            return new Capabilities(
                api: TargetApi.Vulkan,
                vendorName: "Apple",
                memoryType: SystemMemoryType.UnifiedMemory,
                hasFrontFacingBug: false,
                hasVectorIndexingBug: false,
                needsFragmentOutputSpecialization: false,
                reduceShaderPrecision: false,
                supportsAstcCompression: true,
                supportsBc123Compression: false,
                supportsBc45Compression: false,
                supportsBc67Compression: false,
                supportsEtc2Compression: true,
                supports3DTextureCompression: false,
                supportsBgraFormat: true,
                supportsR4G4Format: true,
                supportsR4G4B4A4Format: true,
                supportsScaledVertexFormats: true,
                supportsSnormBufferTextureFormat: true,
                supports5BitComponentFormat: true,
                supportsSparseBuffer: false,
                supportsBlendEquationAdvanced: false,
                supportsFragmentShaderInterlock: false,
                supportsFragmentShaderOrderingIntel: false,
                supportsGeometryShader: true,
                supportsGeometryShaderPassthrough: false,
                supportsTransformFeedback: false,
                supportsImageLoadFormatted: true,
                supportsLayerVertexTessellation: false,
                supportsMismatchingViewFormat: true,
                supportsCubemapView: true,
                supportsNonConstantTextureOffset: true,
                supportsQuads: false,
                supportsSeparateSampler: true,
                supportsShaderBallot: true,
                supportsShaderBarrierDivergence: true,
                supportsShaderFloat64: false,
                supportsShaderNonUniformIndexing: true,
                supportsTextureGatherOffsets: true,
                supportsTextureShadowLod: true,
                supportsVertexStoreAndAtomics: true,
                supportsViewportIndexVertexTessellation: false,
                supportsViewportMask: false,
                supportsViewportSwizzle: false,
                supportsIndirectParameters: false,
                supportsDepthClipControl: true,
                uniformBufferSetIndex: 0,
                storageBufferSetIndex: 1,
                textureSetIndex: 2,
                imageSetIndex: 3,
                extraSetBaseIndex: 4,
                maximumExtraSets: 4,
                maximumUniformBuffersPerStage: 18,
                maximumStorageBuffersPerStage: 16,
                maximumTexturesPerStage: 64,
                maximumImagesPerStage: 8,
                maximumComputeSharedMemorySize: 32768,
                maximumSupportedAnisotropy: 16f,
                shaderSubgroupSize: 32,
                storageBufferOffsetAlignment: 16,
                textureBufferOffsetAlignment: 16,
                gatherBiasPrecision: 8,
                maximumGpuMemory: 24UL * 1024 * 1024 * 1024);
        }
    }
}
