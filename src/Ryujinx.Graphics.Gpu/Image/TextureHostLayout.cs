using Ryujinx.Common;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader.Translation;
using System;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Gpu.Image
{
    readonly struct PagedTextureRegion
    {
        public int Layer { get; }
        public Rectangle<int> Region { get; }
        public int SourceOffset { get; }
        public int SourceLength { get; }

        public PagedTextureRegion(int layer, Rectangle<int> region, int sourceOffset, int sourceLength)
        {
            Layer = layer;
            Region = region;
            SourceOffset = sourceOffset;
            SourceLength = sourceLength;
        }
    }

    readonly struct PagedTextureCopyRegion
    {
        public int SourceLayer { get; }
        public int DestinationLayer { get; }
        public Extents2D SourceRegion { get; }
        public Extents2D DestinationRegion { get; }

        public PagedTextureCopyRegion(
            int sourceLayer,
            int destinationLayer,
            Extents2D sourceRegion,
            Extents2D destinationRegion)
        {
            SourceLayer = sourceLayer;
            DestinationLayer = destinationLayer;
            SourceRegion = sourceRegion;
            DestinationRegion = destinationRegion;
        }
    }

    readonly struct TextureHostLayout
    {
        private readonly struct CopySplit
        {
            public long Numerator { get; }
            public long Denominator { get; }

            public CopySplit(long numerator, long denominator)
            {
                Numerator = numerator;
                Denominator = denominator;
            }
        }

        public const int MetalMaxTexture2DDimension = 16384;
        public const int FoldedLinearTextureGutterX = 1;
        public const int FoldedLinearTextureGutterY = 1;
        private const int BufferBackedLinear2DVersion = 1 << 13;
        private const int PagedLinear2DVersion = 1 << 14;

        // Texel-buffer sampling has caused unrecoverable MoltenVK/AGX stalls on Apple GPUs.
        // Keep the implementation available for unit coverage, but never select it at runtime.
        private const bool BufferBackedLinear2DEnabled = false;

        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int TexelCount { get; }
        public int PageHeight { get; }
        public int PageCount { get; }
        public int HostWidth { get; }
        public int HostHeight { get; }
        public int GutterX { get; }
        public int GutterY { get; }
        public int PageStrideWidth => Width + GutterX * 2;

        private TextureHostLayout(
            int width,
            int height,
            int stride,
            int texelCount,
            int pageHeight = 0,
            int pageCount = 0,
            int hostWidth = 0,
            int hostHeight = 0,
            int gutterX = 0,
            int gutterY = 0)
        {
            Width = width;
            Height = height;
            Stride = stride;
            TexelCount = texelCount;
            PageHeight = pageHeight;
            PageCount = pageCount;
            HostWidth = hostWidth;
            HostHeight = hostHeight;
            GutterX = gutterX;
            GutterY = gutterY;
        }

        public bool IsPaged => PageCount > 1;
        public bool IsFolded => HostWidth > 0 && HostHeight > 0;

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
                info.FormatInfo.Format != Format.R8Unorm ||
                !IsSameHostFormat(info.FormatInfo, hostFormatInfo) ||
                info.Width <= 0 ||
                info.Width > MetalMaxTexture2DDimension ||
                info.Height <= MetalMaxTexture2DDimension ||
                info.Stride < info.Width)
            {
                return false;
            }

            int pageStrideWidth = info.Width + FoldedLinearTextureGutterX * 2;
            int maxPageHeight = MetalMaxTexture2DDimension - FoldedLinearTextureGutterY * 2;
            int minPages = Math.Max(2, BitUtils.DivRoundUp(info.Height, maxPageHeight));
            int maxPages = MetalMaxTexture2DDimension / pageStrideWidth;

            for (int pageCount = minPages; pageCount <= maxPages; pageCount++)
            {
                if (info.Height % pageCount != 0)
                {
                    continue;
                }

                int pageHeight = info.Height / pageCount;
                int hostWidth = pageStrideWidth * pageCount;
                int hostHeight = pageHeight + FoldedLinearTextureGutterY * 2;

                if (hostWidth > MetalMaxTexture2DDimension || hostHeight > MetalMaxTexture2DDimension)
                {
                    continue;
                }

                long texelCount = (long)info.Stride * info.Height;

                if (texelCount > int.MaxValue)
                {
                    return false;
                }

                layout = new TextureHostLayout(
                    info.Width,
                    info.Height,
                    info.Stride,
                    (int)texelCount,
                    pageHeight,
                    pageCount,
                    hostWidth,
                    hostHeight,
                    FoldedLinearTextureGutterX,
                    FoldedLinearTextureGutterY);

                return true;
            }

            return false;
        }

        internal PagedTextureRegion[] GetPagedRegions(
            Rectangle<int> region,
            int dataLength,
            int bytesPerPixel)
        {
            if (!IsPaged)
            {
                throw new InvalidOperationException("Texture layout is not paged.");
            }

            if (region.X < 0 ||
                region.Y < 0 ||
                region.Width <= 0 ||
                region.Height <= 0 ||
                region.X + region.Width > Width ||
                region.Y + region.Height > Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(region),
                    $"Paged texture region {region.X},{region.Y} {region.Width}x{region.Height} is outside {Width}x{Height}.");
            }

            if (bytesPerPixel <= 0 || dataLength <= 0 || dataLength % region.Height != 0)
            {
                throw new ArgumentException("Paged texture data does not contain a whole number of rows.", nameof(dataLength));
            }

            int sourceRowStride = dataLength / region.Height;
            int minimumRowStride = checked(region.Width * bytesPerPixel);

            if (sourceRowStride < minimumRowStride)
            {
                throw new ArgumentException(
                    $"Paged texture row stride {sourceRowStride} is smaller than the required {minimumRowStride} bytes.",
                    nameof(dataLength));
            }

            int firstPage = region.Y / PageHeight;
            int lastPage = (region.Y + region.Height - 1) / PageHeight;
            PagedTextureRegion[] regions = new PagedTextureRegion[lastPage - firstPage + 1];
            int sourceRow = 0;

            for (int index = 0; index < regions.Length; index++)
            {
                int page = firstPage + index;
                int pageStartY = page * PageHeight;
                int pageY = Math.Max(region.Y, pageStartY);
                int pageEndY = Math.Min(region.Y + region.Height, pageStartY + PageHeight);
                int rows = pageEndY - pageY;
                int sourceOffset = checked(sourceRow * sourceRowStride);
                int sourceLength = checked(rows * sourceRowStride);

                regions[index] = new PagedTextureRegion(
                    page,
                    new Rectangle<int>(region.X, pageY - pageStartY, region.Width, rows),
                    sourceOffset,
                    sourceLength);

                sourceRow += rows;
            }

            return regions;
        }

        internal static PagedTextureCopyRegion[] GetPagedCopyRegions(
            TextureHostLayout? sourceLayout,
            TextureHostLayout? destinationLayout,
            Extents2D sourceRegion,
            Extents2D destinationRegion)
        {
            if (!sourceLayout.HasValue && !destinationLayout.HasValue)
            {
                throw new ArgumentException("At least one texture copy layout must be paged.");
            }

            if (sourceLayout.HasValue)
            {
                ValidateCopyRegion(sourceLayout.Value, sourceRegion, nameof(sourceRegion));
            }

            if (destinationLayout.HasValue)
            {
                ValidateCopyRegion(destinationLayout.Value, destinationRegion, nameof(destinationRegion));
            }

            int sourceHeight = sourceRegion.Y2 - sourceRegion.Y1;
            int destinationHeight = destinationRegion.Y2 - destinationRegion.Y1;

            if (sourceHeight <= 0 || destinationHeight <= 0 ||
                sourceRegion.X2 <= sourceRegion.X1 || destinationRegion.X2 <= destinationRegion.X1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceRegion),
                    "Paged texture copies require positive source and destination regions.");
            }

            List<CopySplit> splits =
            [
                new CopySplit(0, 1),
                new CopySplit(1, 1),
            ];

            AddPageSplits(splits, sourceLayout, sourceRegion.Y1, sourceRegion.Y2);
            AddPageSplits(splits, destinationLayout, destinationRegion.Y1, destinationRegion.Y2);

            splits.Sort(static (left, right) =>
                (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator));

            List<CopySplit> uniqueSplits = new(splits.Count);

            foreach (CopySplit split in splits)
            {
                if (uniqueSplits.Count == 0 || !AreEqual(uniqueSplits[^1], split))
                {
                    uniqueSplits.Add(split);
                }
            }

            PagedTextureCopyRegion[] regions = new PagedTextureCopyRegion[uniqueSplits.Count - 1];

            for (int index = 0; index < regions.Length; index++)
            {
                CopySplit start = uniqueSplits[index];
                CopySplit end = uniqueSplits[index + 1];

                int sourceY1 = MapCoordinate(sourceRegion.Y1, sourceHeight, start);
                int sourceY2 = MapCoordinate(sourceRegion.Y1, sourceHeight, end);
                int destinationY1 = MapCoordinate(destinationRegion.Y1, destinationHeight, start);
                int destinationY2 = MapCoordinate(destinationRegion.Y1, destinationHeight, end);

                if (sourceY2 <= sourceY1 || destinationY2 <= destinationY1)
                {
                    throw new InvalidOperationException(
                        "Paged texture copy scaling collapses a page segment; refusing unsafe GPU submission.");
                }

                int sourceLayer = GetLayerAndLocalY(sourceLayout, sourceY1, out int sourceLocalY1);
                int sourceEndLayer = GetLayerAndLocalY(sourceLayout, sourceY2 - 1, out int sourceLocalY2Inclusive);

                int destinationLayer = GetLayerAndLocalY(destinationLayout, destinationY1, out int destinationLocalY1);
                int destinationEndLayer = GetLayerAndLocalY(destinationLayout, destinationY2 - 1, out int destinationLocalY2Inclusive);

                if (sourceLayer != sourceEndLayer || destinationLayer != destinationEndLayer)
                {
                    throw new InvalidOperationException(
                        "Paged texture copy segment still crosses a host page; refusing unsafe GPU submission.");
                }

                regions[index] = new PagedTextureCopyRegion(
                    sourceLayer,
                    destinationLayer,
                    new Extents2D(
                        sourceRegion.X1,
                        sourceLocalY1,
                        sourceRegion.X2,
                        sourceLocalY2Inclusive + 1),
                    new Extents2D(
                        destinationRegion.X1,
                        destinationLocalY1,
                        destinationRegion.X2,
                        destinationLocalY2Inclusive + 1));
            }

            return regions;
        }

        private static void AddPageSplits(
            List<CopySplit> splits,
            TextureHostLayout? layout,
            int regionY1,
            int regionY2)
        {
            if (!layout.HasValue)
            {
                return;
            }

            TextureHostLayout value = layout.Value;
            int regionHeight = regionY2 - regionY1;

            for (int boundary = value.PageHeight; boundary < value.Height; boundary += value.PageHeight)
            {
                if (boundary > regionY1 && boundary < regionY2)
                {
                    splits.Add(new CopySplit(boundary - regionY1, regionHeight));
                }
            }
        }

        private static bool AreEqual(CopySplit left, CopySplit right)
        {
            return left.Numerator * right.Denominator == right.Numerator * left.Denominator;
        }

        private static int MapCoordinate(int start, int length, CopySplit split)
        {
            long scaled = checked((long)length * split.Numerator);
            long rounded = checked((scaled + split.Denominator / 2) / split.Denominator);

            return checked(start + (int)rounded);
        }

        private static int GetLayerAndLocalY(TextureHostLayout? layout, int y, out int localY)
        {
            if (!layout.HasValue)
            {
                localY = y;
                return 0;
            }

            TextureHostLayout value = layout.Value;
            int layer = y / value.PageHeight;
            localY = y - layer * value.PageHeight;

            return layer;
        }

        private static void ValidateCopyRegion(TextureHostLayout layout, Extents2D region, string parameterName)
        {
            if (region.X1 < 0 ||
                region.Y1 < 0 ||
                region.X2 <= region.X1 ||
                region.Y2 <= region.Y1 ||
                region.X2 > layout.Width ||
                region.Y2 > layout.Height)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Paged texture copy region ({region.X1},{region.Y1})-({region.X2},{region.Y2}) " +
                    $"is outside {layout.Width}x{layout.Height}.");
            }
        }

        public static bool TryGetPagedLinear2D(
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
                info.Height % MetalMaxTexture2DDimension != 0 ||
                info.Stride < info.Width)
            {
                return false;
            }

            int pageCount = info.Height / MetalMaxTexture2DDimension;

            if (pageCount <= 1)
            {
                return false;
            }

            long texelCount = (long)info.Stride * info.Height;

            if (texelCount > int.MaxValue)
            {
                return false;
            }

            layout = new TextureHostLayout(
                info.Width,
                info.Height,
                info.Stride,
                (int)texelCount,
                MetalMaxTexture2DDimension,
                pageCount);

            return true;
        }

        public static bool TryGetBufferBackedLinear2D(
            TextureInfo info,
            Capabilities caps,
            FormatInfo hostFormatInfo,
            float scale,
            out TextureHostLayout layout)
        {
            return TryGetBufferBackedLinear2D(info, caps, hostFormatInfo, scale, BufferBackedLinear2DEnabled, out layout);
        }

        internal static bool TryGetBufferBackedLinear2D(
            TextureInfo info,
            Capabilities caps,
            FormatInfo hostFormatInfo,
            float scale,
            bool bufferBackedEnabled,
            out TextureHostLayout layout)
        {
            layout = default;

            if (!bufferBackedEnabled ||
                !OperatingSystem.IsMacOS() ||
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

            if (TryGetFoldedLinear2D(info, caps, hostFormatInfo, scale, out _))
            {
                return false;
            }

            layout = new TextureHostLayout(info.Width, info.Height, info.Stride, (int)texelCount);

            return true;
        }

        public static int GetBufferBackedLinear2DState(in TextureDescriptor descriptor, bool isVulkan)
        {
            return GetBufferBackedLinear2DState(in descriptor, isVulkan, BufferBackedLinear2DEnabled);
        }

        public static int GetPagedLinear2DState(in TextureDescriptor descriptor, bool isVulkan)
        {
            TextureTarget target = descriptor.UnpackTextureTarget();

            if (!OperatingSystem.IsMacOS() ||
                !isVulkan ||
                IsFoldedLinear2DDescriptor(descriptor) ||
                descriptor.UnpackTextureDescriptorType() != TextureDescriptorType.Linear ||
                target is not (TextureTarget.Texture2D or TextureTarget.Texture2DRect) ||
                descriptor.UnpackLevels() != 1 ||
                descriptor.UnpackWidth() <= 0 ||
                descriptor.UnpackWidth() > MetalMaxTexture2DDimension ||
                descriptor.UnpackHeight() <= MetalMaxTexture2DDimension ||
                descriptor.UnpackHeight() % MetalMaxTexture2DDimension != 0 ||
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

            // Keep the page layout in the specialization state so cached shaders are
            // invalidated if the host representation changes.
            return PagedLinear2DVersion | (swizzle << 1) | 1;
        }

        internal static bool IsFoldedLinear2DDescriptor(in TextureDescriptor descriptor)
        {
            TextureTarget target = descriptor.UnpackTextureTarget();

            if (descriptor.UnpackTextureDescriptorType() != TextureDescriptorType.Linear ||
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
                return false;
            }

            int pageStrideWidth = descriptor.UnpackWidth() + FoldedLinearTextureGutterX * 2;
            int maxPageHeight = MetalMaxTexture2DDimension - FoldedLinearTextureGutterY * 2;
            int minPages = Math.Max(2, BitUtils.DivRoundUp(descriptor.UnpackHeight(), maxPageHeight));
            int maxPages = MetalMaxTexture2DDimension / pageStrideWidth;

            for (int pageCount = minPages; pageCount <= maxPages; pageCount++)
            {
                if (descriptor.UnpackHeight() % pageCount == 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPagedLinear2DState(int state)
        {
            return (state & PagedLinear2DVersion) != 0;
        }

        internal static int GetBufferBackedLinear2DState(
            in TextureDescriptor descriptor,
            bool isVulkan,
            bool bufferBackedEnabled)
        {
            TextureTarget target = descriptor.UnpackTextureTarget();

            if (!bufferBackedEnabled ||
                !OperatingSystem.IsMacOS() ||
                !isVulkan ||
                IsFoldedLinear2DDescriptor(descriptor) ||
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
