using NUnit.Framework;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Tests.Graphics.Shader
{
    public class AttributeUsageTests
    {
        [Test]
        public void GeometryPassthroughInputsArePropagatedToVertexOutputs()
        {
            const int geometryInputLocation = 1;
            const int passthroughLocation = 3;

            AttributeUsage fragmentUsage = new(null);
            fragmentUsage.SetInputUserAttribute(passthroughLocation, 0);

            AttributeUsage geometryUsage = new(null);
            geometryUsage.SetInputUserAttribute(geometryInputLocation, 0);
            geometryUsage.MergeFromtNextStage(
                gpPassthrough: true,
                nextUsesFixedFunctionAttributes: false,
                fragmentUsage);

            AttributeUsage vertexUsage = new(null);
            vertexUsage.MergeFromtNextStage(
                gpPassthrough: false,
                nextUsesFixedFunctionAttributes: false,
                geometryUsage);

            int expectedMask = (1 << geometryInputLocation) | (1 << passthroughLocation);

            Assert.That(geometryUsage.PassthroughAttributes, Is.EqualTo(1 << passthroughLocation));
            Assert.That(vertexUsage.UsedOutputAttributes & expectedMask, Is.EqualTo(expectedMask));

            IoUsage linkedVertexOutput = new(FeatureFlags.None, 0, vertexUsage.UsedOutputAttributes);
            ResourceReservations vertexReservations = new(
                gpuAccessor: null,
                isTransformFeedbackEmulated: false,
                vertexAsCompute: true,
                vacInput: null,
                linkedVertexOutput);
            ResourceReservations geometryReservations = new(
                gpuAccessor: null,
                isTransformFeedbackEmulated: false,
                vertexAsCompute: true,
                linkedVertexOutput,
                linkedVertexOutput);

            Assert.That(vertexReservations.OutputSizePerInvocation, Is.EqualTo(13));
            Assert.That(
                geometryReservations.InputSizePerInvocation,
                Is.EqualTo(vertexReservations.OutputSizePerInvocation));
        }

        [Test]
        public void ViewportMaskFallbackReservesViewportIndexForGeometryAsCompute()
        {
            IGpuAccessor gpuAccessor = new ViewportFallbackGpuAccessor(
                supportsViewportIndexVertexTessellation: true,
                supportsViewportMask: false);
            IoUsage viewportMaskOutput = new(FeatureFlags.ViewportMask, 0, 0);

            ResourceReservations reservations = new(
                gpuAccessor,
                false,
                true,
                null,
                viewportMaskOutput);

            Assert.That(
                reservations.TryGetOffset(StorageKind.Output, IoVariable.ViewportIndex, out int viewportIndexOffset),
                Is.True);
            Assert.That(
                reservations.TryGetOffset(StorageKind.Output, IoVariable.ViewportMask, out _),
                Is.False);
            Assert.That(viewportIndexOffset, Is.EqualTo(5));
        }

        [Test]
        public void UnsupportedViewportIndexDoesNotReserveFallbackOutput()
        {
            IGpuAccessor gpuAccessor = new ViewportFallbackGpuAccessor(
                supportsViewportIndexVertexTessellation: false,
                supportsViewportMask: false);
            IoUsage viewportMaskOutput = new(FeatureFlags.ViewportMask, 0, 0);

            ResourceReservations reservations = new(
                gpuAccessor,
                false,
                true,
                null,
                viewportMaskOutput);

            Assert.That(
                reservations.TryGetOffset(StorageKind.Output, IoVariable.ViewportIndex, out _),
                Is.False);
        }

        private sealed class ViewportFallbackGpuAccessor : IGpuAccessor
        {
            private readonly bool _supportsViewportIndexVertexTessellation;
            private readonly bool _supportsViewportMask;

            public ViewportFallbackGpuAccessor(bool supportsViewportIndexVertexTessellation, bool supportsViewportMask)
            {
                _supportsViewportIndexVertexTessellation = supportsViewportIndexVertexTessellation;
                _supportsViewportMask = supportsViewportMask;
            }

            public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize)
            {
                return ReadOnlySpan<ulong>.Empty;
            }

            public SetBindingPair CreateConstantBufferBinding(int index)
            {
                return new SetBindingPair(0, index);
            }

            public SetBindingPair CreateImageBinding(int count, bool isBuffer)
            {
                return new SetBindingPair(3, 0);
            }

            public SetBindingPair CreateStorageBufferBinding(int index)
            {
                return new SetBindingPair(1, index);
            }

            public SetBindingPair CreateTextureBinding(int count, bool isBuffer)
            {
                return new SetBindingPair(2, 0);
            }

            public bool QueryHostSupportsViewportIndexVertexTessellation()
            {
                return _supportsViewportIndexVertexTessellation;
            }

            public bool QueryHostSupportsViewportMask()
            {
                return _supportsViewportMask;
            }

            public int QuerySamplerArrayLengthFromPool()
            {
                return 1;
            }

            public int QueryTextureArrayLengthFromBuffer(int slot)
            {
                return 1;
            }

            public int QueryTextureArrayLengthFromPool()
            {
                return 1;
            }
        }
    }
}
