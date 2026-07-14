using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Graphics.Gpu.Image
{
    readonly struct TextureHostLayout
    {
        public const int MetalMaxTexture2DDimension = 16384;
        private const int BufferBackedLinear2DVersion = 1 << 13;

        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int TexelCount { get; }

        private TextureHostLayout(int width, int height, int stride, int texelCount)
        {
            Width = width;
            Height = height;
            Stride = stride;
            TexelCount = texelCount;
        }

        public static bool TryGetBufferBackedLinear2D(
            TextureInfo info,
            Capabilities caps,
            FormatInfo hostFormatInfo,
            float scale,
            out TextureHostLayout layout)
        {
            layout = default;

            if (!OperatingSystem.IsMacOS() ||
                caps.Api != TargetApi.Vulkan ||
                scale != 1f ||
                !info.IsLinear ||
                info.Target != Target.Texture2D ||
                info.Levels != 1 ||
                info.GetLayers() != 1 ||
                info.GetDepth() != 1 ||
                info.Samples != 1 ||
                info.FormatInfo.IsCompressed ||
                info.FormatInfo.Format != Format.R8Unorm ||
                !IsSameHostFormat(info.FormatInfo, hostFormatInfo) ||
                info.Width <= 0 ||
                info.Width > MetalMaxTexture2DDimension ||
                info.Height <= MetalMaxTexture2DDimension ||
                info.Stride < info.Width)
            {
                return false;
            }

            long texelCount = (long)info.Stride * info.Height;

            if (texelCount > int.MaxValue)
            {
                return false;
            }

            layout = new TextureHostLayout(info.Width, info.Height, info.Stride, (int)texelCount);

            return true;
        }

        public static int GetBufferBackedLinear2DState(in TextureDescriptor descriptor, bool isVulkan)
        {
            TextureTarget target = descriptor.UnpackTextureTarget();

            if (!OperatingSystem.IsMacOS() ||
                !isVulkan ||
                descriptor.UnpackTextureDescriptorType() != TextureDescriptorType.Linear ||
                target is not (TextureTarget.Texture2D or TextureTarget.Texture2DRect) ||
                descriptor.UnpackLevels() != 1 ||
                descriptor.UnpackWidth() <= 0 ||
                descriptor.UnpackWidth() > MetalMaxTexture2DDimension ||
                descriptor.UnpackHeight() <= MetalMaxTexture2DDimension ||
                descriptor.UnpackStride() < descriptor.UnpackWidth() ||
                descriptor.UnpackSrgb() ||
                !FormatTable.TryGetTextureFormat(descriptor.UnpackFormat(), false, out FormatInfo formatInfo) ||
                formatInfo.Format != Format.R8Unorm)
            {
                return 0;
            }

            int swizzle = (int)descriptor.UnpackSwizzleR() |
                ((int)descriptor.UnpackSwizzleG() << 3) |
                ((int)descriptor.UnpackSwizzleB() << 6) |
                ((int)descriptor.UnpackSwizzleA() << 9);

            // Keep a local code generation version in the specialization state so only shaders
            // using this path are invalidated when its sampling implementation changes.
            return BufferBackedLinear2DVersion | (swizzle << 1) | 1;
        }

        private static bool IsSameHostFormat(FormatInfo guestFormatInfo, FormatInfo hostFormatInfo)
        {
            return guestFormatInfo.Format == hostFormatInfo.Format &&
                   guestFormatInfo.BlockWidth == hostFormatInfo.BlockWidth &&
                   guestFormatInfo.BlockHeight == hostFormatInfo.BlockHeight &&
                   guestFormatInfo.BytesPerPixel == hostFormatInfo.BytesPerPixel &&
                   guestFormatInfo.Components == hostFormatInfo.Components;
        }
    }
}
