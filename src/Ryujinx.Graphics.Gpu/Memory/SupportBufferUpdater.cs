using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Shader;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Graphics.Gpu.Memory
{
    /// <summary>
    /// Support buffer data updater.
    /// </summary>
    class SupportBufferUpdater : BufferUpdater
    {
        private static readonly bool FoldedTextureDiagnostics =
            string.Equals(Environment.GetEnvironmentVariable("RYUJINX_FOLDED_DIAGNOSTICS"), "1", StringComparison.Ordinal);
        private static readonly HashSet<string> FoldedRenderScaleDiagnosticKeys = [];

        private SupportBuffer _data;

        /// <summary>
        /// Creates a new instance of the support buffer updater.
        /// </summary>
        /// <param name="renderer">Renderer that the support buffer will be used with</param>
        public SupportBufferUpdater(IRenderer renderer) : base(renderer)
        {
            Vector4<float> defaultScale = new() { X = 1f, Y = 0f, Z = 0f, W = 0f };
            _data.RenderScale.AsSpan().Fill(defaultScale);
            DirtyRenderScale(0, SupportBuffer.RenderScaleMaxCount);
        }

        /// <summary>
        /// Marks the fragment render scale count as being modified.
        /// </summary>
        private void DirtyFragmentRenderScaleCount()
        {
            MarkDirty(SupportBuffer.FragmentRenderScaleCountOffset, sizeof(int));
        }

        /// <summary>
        /// Marks data of a given type as being modified.
        /// </summary>
        /// <typeparam name="T">Type of the data</typeparam>
        /// <param name="baseOffset">Base offset of the data in bytes</param>
        /// <param name="offset">Index of the data, in elements</param>
        /// <param name="count">Number of elements</param>
        private void DirtyGenericField<T>(int baseOffset, int offset, int count) where T : unmanaged
        {
            int elemSize = Unsafe.SizeOf<T>();

            MarkDirty(baseOffset + offset * elemSize, count * elemSize);
        }

        /// <summary>
        /// Marks render scales as being modified.
        /// </summary>
        /// <param name="offset">Index of the first scale that was modified</param>
        /// <param name="count">Number of modified scales</param>
        private void DirtyRenderScale(int offset, int count)
        {
            DirtyGenericField<Vector4<float>>(SupportBuffer.GraphicsRenderScaleOffset, offset, count);
        }

        /// <summary>
        /// Marks render target BGRA format state as modified.
        /// </summary>
        /// <param name="offset">Index of the first render target that had its BGRA format modified</param>
        /// <param name="count">Number of render targets</param>
        private void DirtyFragmentIsBgra(int offset, int count)
        {
            DirtyGenericField<Vector4<int>>(SupportBuffer.FragmentIsBgraOffset, offset, count);
        }

        /// <summary>
        /// Updates the inverse viewport vector.
        /// </summary>
        /// <param name="data">Inverse viewport vector</param>
        private void UpdateViewportInverse(Vector4<float> data)
        {
            _data.ViewportInverse = data;

            MarkDirty(SupportBuffer.ViewportInverseOffset, SupportBuffer.FieldSize);
        }

        /// <summary>
        /// Updates the viewport size vector.
        /// </summary>
        /// <param name="data">Viewport size vector</param>
        private void UpdateViewportSize(Vector4<float> data)
        {
            _data.ViewportSize = data;

            MarkDirty(SupportBuffer.ViewportSizeOffset, SupportBuffer.FieldSize);
        }

        /// <summary>
        /// Sets the scale of all output render targets (they should all have the same scale).
        /// </summary>
        /// <param name="scale">Scale value</param>
        public void SetRenderTargetScale(float scale)
        {
            _data.RenderScale[0].X = scale;
            DirtyRenderScale(0, 1); // Just the first element.
        }

        /// <summary>
        /// Updates the render scales for shader input textures or images.
        /// </summary>
        /// <param name="index">Index of the scale</param>
        /// <param name="scale">Scale value</param>
        public void UpdateRenderScale(int index, float scale)
        {
            UpdateRenderScale(index, new Vector4<float> { X = scale });
        }

        /// <summary>
        /// Updates the render scale and texture layout metadata for shader input textures or images.
        /// </summary>
        /// <param name="index">Index of the scale</param>
        /// <param name="data">Scale and texture layout metadata</param>
        public void UpdateRenderScale(int index, Vector4<float> data)
        {
            Span<Vector4<float>> renderScaleSpan = _data.RenderScale.AsSpan();
            Vector4<float> previousData = renderScaleSpan[1 + index];

            if (previousData.X != data.X ||
                previousData.Y != data.Y ||
                previousData.Z != data.Z ||
                previousData.W != data.W)
            {
                LogFoldedRenderScaleUpdate(index, previousData, data);

                renderScaleSpan[1 + index] = data;
                DirtyRenderScale(1 + index, 1);
            }
        }

        private static void LogFoldedRenderScaleUpdate(int index, Vector4<float> previousData, Vector4<float> data)
        {
            if (!FoldedTextureDiagnostics ||
                (previousData.Y == 0f && data.Y == 0f))
            {
                return;
            }

            string previous = FormatRenderScale(previousData);
            string next = FormatRenderScale(data);
            string key = $"support-render-scale:{index}:{previous}:{next}";

            if (FoldedRenderScaleDiagnosticKeys.Add(key))
            {
                Logger.Warning?.Print(
                    LogClass.Gpu,
                    $"Folded diagnostic support buffer render_scale update: logicalIndex={index}, storageIndex={1 + index}, " +
                    $"old={previous}, new={next}.");
            }
        }

        private static string FormatRenderScale(Vector4<float> renderScale)
        {
            return $"({renderScale.X:R},{renderScale.Y:R},{renderScale.Z:R},{renderScale.W:R})";
        }

        /// <summary>
        /// Updates the render scales for shader input textures or images.
        /// </summary>
        /// <param name="totalCount">Total number of scales across all stages</param>
        /// <param name="fragmentCount">Total number of scales on the fragment shader stage</param>
        public void UpdateRenderScaleFragmentCount(int totalCount, int fragmentCount)
        {
            // Only update fragment count if there are scales after it for the vertex stage.
            if (fragmentCount != totalCount && fragmentCount != _data.FragmentRenderScaleCount.X)
            {
                LogFoldedFragmentScaleCountUpdate(totalCount, fragmentCount, _data.FragmentRenderScaleCount.X);

                _data.FragmentRenderScaleCount.X = fragmentCount;
                DirtyFragmentRenderScaleCount();
            }
        }

        private static void LogFoldedFragmentScaleCountUpdate(int totalCount, int fragmentCount, int previousFragmentCount)
        {
            if (!FoldedTextureDiagnostics)
            {
                return;
            }

            string key = $"fragment-scale-count:{totalCount}:{fragmentCount}:{previousFragmentCount}";

            if (FoldedRenderScaleDiagnosticKeys.Add(key))
            {
                Logger.Warning?.Print(
                    LogClass.Gpu,
                    $"Folded diagnostic support buffer fragment scale count update: total={totalCount}, " +
                    $"fragment={fragmentCount}, oldFragment={previousFragmentCount}.");
            }
        }

        /// <summary>
        /// Sets whether the format of a given render target is a BGRA format.
        /// </summary>
        /// <param name="index">Render target index</param>
        /// <param name="isBgra">True if the format is BGRA< false otherwise</param>
        public void SetRenderTargetIsBgra(int index, bool isBgra)
        {
            Span<Vector4<int>> fragmentIsBgraSpan = _data.FragmentIsBgra.AsSpan();
            
            bool isBgraChanged = fragmentIsBgraSpan[index].X != 0 != isBgra;

            if (isBgraChanged)
            {
                fragmentIsBgraSpan[index].X = isBgra ? 1 : 0;
                DirtyFragmentIsBgra(index, 1);
            }
        }

        /// <summary>
        /// Sets whether a viewport has transform disabled.
        /// </summary>
        /// <param name="viewportWidth">Value used as viewport width</param>
        /// <param name="viewportHeight">Value used as viewport height</param>
        /// <param name="scale">Render target scale</param>
        /// <param name="disableTransform">True if transform is disabled, false otherwise</param>
        public void SetViewportTransformDisable(float viewportWidth, float viewportHeight, float scale, bool disableTransform)
        {
            float disableTransformF = disableTransform ? 1.0f : 0.0f;
            if (_data.ViewportInverse.W != disableTransformF || disableTransform)
            {
                UpdateViewportInverse(new Vector4<float>
                {
                    X = scale * 2f / viewportWidth,
                    Y = scale * 2f / viewportHeight,
                    Z = 1,
                    W = disableTransformF,
                });
            }
        }

        /// <summary>
        /// Sets the viewport size, used to invert the fragment coordinates Y value.
        /// </summary>
        /// <param name="viewportWidth">Value used as viewport width</param>
        /// <param name="viewportHeight">Value used as viewport height</param>
        public void SetViewportSize(float viewportWidth, float viewportHeight)
        {
            if (_data.ViewportSize.X != viewportWidth || _data.ViewportSize.Y != viewportHeight)
            {
                UpdateViewportSize(new Vector4<float>
                {
                    X = viewportWidth,
                    Y = viewportHeight,
                    Z = 1,
                    W = 0
                });
            }
        }

        /// <summary>
        /// Sets offset for the misaligned portion of a transform feedback buffer, and the buffer size, for transform feedback emulation.
        /// </summary>
        /// <param name="bufferIndex">Index of the transform feedback buffer</param>
        /// <param name="offset">Misaligned offset of the buffer</param>
        public void SetTfeOffset(int bufferIndex, int offset)
        {
            ref int currentOffset = ref GetElementRef(ref _data.TfeOffset, bufferIndex);

            if (currentOffset != offset)
            {
                currentOffset = offset;
                MarkDirty(SupportBuffer.TfeOffsetOffset + bufferIndex * sizeof(int), sizeof(int));
            }
        }

        /// <summary>
        /// Sets the vertex count used for transform feedback emulation with instanced draws.
        /// </summary>
        /// <param name="vertexCount">Vertex count of the instanced draw</param>
        public void SetTfeVertexCount(int vertexCount)
        {
            if (_data.TfeVertexCount.X != vertexCount)
            {
                _data.TfeVertexCount.X = vertexCount;
                MarkDirty(SupportBuffer.TfeVertexCountOffset, sizeof(int));
            }
        }

        /// <summary>
        /// Sets the guest base address and size for an indirect storage-buffer target.
        /// </summary>
        /// <param name="stage">Graphics shader stage index</param>
        /// <param name="targetIndex">Target index within the stage</param>
        /// <param name="address">Aligned guest GPU virtual address</param>
        /// <param name="size">Mapped target size in bytes</param>
        /// <param name="valid">Whether the target is valid</param>
        public void SetIndirectStorageTarget(int stage, int targetIndex, ulong address, uint size, bool valid)
        {
            int index = SupportBuffer.GetIndirectStorageTargetIndex(stage, targetIndex);
            Span<Vector4<uint>> targets = _data.IndirectStorageTargets.AsSpan();
            Vector4<uint> value = new()
            {
                X = (uint)address,
                Y = (uint)(address >> 32),
                Z = size,
                W = valid ? 1u : 0u,
            };

            if (targets[index].X != value.X ||
                targets[index].Y != value.Y ||
                targets[index].Z != value.Z ||
                targets[index].W != value.W)
            {
                targets[index] = value;
                DirtyGenericField<Vector4<uint>>(SupportBuffer.IndirectStorageTargetsOffset, index, 1);
            }
        }

        /// <summary>
        /// Submits all pending buffer updates to the GPU.
        /// </summary>
        public void Commit()
        {
            Commit(MemoryMarshal.Cast<SupportBuffer, byte>(MemoryMarshal.CreateSpan(ref _data, 1)), SupportBuffer.Binding);
        }
    }
}
