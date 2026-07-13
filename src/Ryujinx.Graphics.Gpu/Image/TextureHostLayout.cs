using Ryujinx.Common;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Graphics.Gpu.Image
{
    readonly struct TextureHostLayout
    {
        public const int MetalMaxTexture2DDimension = 16384;
        public const int FoldedLinearTextureGutterX = 1;
        public const int FoldedLinearTextureGutterY = 0;

        public int LogicalWidth { get; }
        public int LogicalHeight { get; }
        public int HostWidth { get; }
        public int HostHeight { get; }
        public int PageHeight { get; }
        public int Pages { get; }
        public int GutterX { get; }
        public int GutterY { get; }
        public int PageStrideWidth => LogicalWidth + GutterX * 2;

        public bool IsFolded => Pages > 1;

        private TextureHostLayout(
            int logicalWidth,
            int logicalHeight,
            int hostWidth,
            int hostHeight,
            int pageHeight,
            int pages,
            int gutterX,
            int gutterY)
        {
            LogicalWidth = logicalWidth;
            LogicalHeight = logicalHeight;
            HostWidth = hostWidth;
            HostHeight = hostHeight;
            PageHeight = pageHeight;
            Pages = pages;
            GutterX = gutterX;
            GutterY = gutterY;
        }

        public static bool TryGetFoldedLinear2D(
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
                !IsSameHostFormat(info.FormatInfo, hostFormatInfo))
            {
                return false;
            }

            int width = info.Width;
            int height = info.Height;
            int gutterX = FoldedLinearTextureGutterX;
            int gutterY = FoldedLinearTextureGutterY;
            int pageStrideWidth = width + gutterX * 2;
            int maxPageHeight = MetalMaxTexture2DDimension - gutterY * 2;

            if (width <= 0 ||
                height <= MetalMaxTexture2DDimension ||
                pageStrideWidth > MetalMaxTexture2DDimension ||
                maxPageHeight <= 0)
            {
                return false;
            }

            int minPages = Math.Max(2, BitUtils.DivRoundUp(height, maxPageHeight));
            int maxPages = MetalMaxTexture2DDimension / pageStrideWidth;

            for (int pages = minPages; pages <= maxPages; pages++)
            {
                if (height % pages != 0)
                {
                    continue;
                }

                int pageHeight = height / pages;
                int hostWidth = pageStrideWidth * pages;
                int hostHeight = pageHeight + gutterY * 2;

                if (hostWidth <= MetalMaxTexture2DDimension &&
                    hostHeight <= MetalMaxTexture2DDimension)
                {
                    layout = new TextureHostLayout(
                        width,
                        height,
                        hostWidth,
                        hostHeight,
                        pageHeight,
                        pages,
                        gutterX,
                        gutterY);

                    return true;
                }
            }

            return false;
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
