using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Vulkan;
using System;

namespace Ryujinx.Tests.Graphics.Vulkan
{
    public class VulkanTextureDescriptorTests
    {
        [Test]
        public void MoltenVkRejectsOversizedTwoDimensionalHostImages()
        {
            TextureCreateInfo info = CreateInfo(32768, 32768, Target.Texture2D);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => VulkanRenderer.ValidateTextureCreateInfo(info, isMoltenVk: true));

            Assert.That(exception.Message, Does.Contain("rejected before vkCreateImage"));
        }

        [Test]
        public void NonMoltenVkKeepsHostImageDimensions()
        {
            TextureCreateInfo info = CreateInfo(32768, 32768, Target.Texture2D);

            TextureCreateInfo normalized = VulkanRenderer.ValidateTextureCreateInfo(info, isMoltenVk: false);

            Assert.That(normalized.Width, Is.EqualTo(32768));
            Assert.That(normalized.Height, Is.EqualTo(32768));
        }

        [Test]
        public void PagedTextureArrayIsAlreadyWithinMetalLimits()
        {
            TextureCreateInfo info = CreateInfo(1024, 16384, Target.Texture2DArray, depth: 2);

            TextureCreateInfo normalized = VulkanRenderer.ValidateTextureCreateInfo(info, isMoltenVk: true);

            Assert.That(normalized, Is.EqualTo(info));
        }

        [Test]
        public void MoltenVkRejectsOversizedThreeDimensionalHostImages()
        {
            TextureCreateInfo info = CreateInfo(2048, 2048, Target.Texture3D, depth: 4096);

            Assert.Throws<InvalidOperationException>(
                () => VulkanRenderer.ValidateTextureCreateInfo(info, isMoltenVk: true));
        }

        [Test]
        public void MoltenVkRejectsOversizedArrayLayerCount()
        {
            TextureCreateInfo info = CreateInfo(64, 64, Target.Texture2DArray, depth: 4096);

            Assert.Throws<InvalidOperationException>(
                () => VulkanRenderer.ValidateTextureCreateInfo(info, isMoltenVk: true));
        }

        private static TextureCreateInfo CreateInfo(int width, int height, Target target, int depth = 1)
        {
            return new TextureCreateInfo(
                width,
                height,
                depth,
                levels: 1,
                samples: 1,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerPixel: 1,
                format: Format.R8Unorm,
                depthStencilMode: DepthStencilMode.Depth,
                target,
                swizzleR: SwizzleComponent.Red,
                swizzleG: SwizzleComponent.Green,
                swizzleB: SwizzleComponent.Blue,
                swizzleA: SwizzleComponent.Alpha);
        }
    }
}
