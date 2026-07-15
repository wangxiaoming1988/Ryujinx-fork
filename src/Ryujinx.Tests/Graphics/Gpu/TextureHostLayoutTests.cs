using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class TextureHostLayoutTests
    {
        [Test]
        public void OversizedLinearR8TextureUsesBufferBackedHostLayout()
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

            Assert.That(TextureHostLayout.TryGetBufferBackedLinear2D(info, caps, formatInfo, 1f, out TextureHostLayout layout), Is.True);
            Assert.That(layout.Width, Is.EqualTo(1024));
            Assert.That(layout.Height, Is.EqualTo(32768));
            Assert.That(layout.Stride, Is.EqualTo(1024));
            Assert.That(layout.TexelCount, Is.EqualTo(33554432));

            TextureCreateInfo createInfo = TextureCache.GetCreateInfo(info, caps, 1f);

            Assert.That(createInfo.Target, Is.EqualTo(Target.TextureBuffer));
            Assert.That(createInfo.Width, Is.EqualTo(layout.TexelCount));
            Assert.That(createInfo.Height, Is.EqualTo(1));
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
