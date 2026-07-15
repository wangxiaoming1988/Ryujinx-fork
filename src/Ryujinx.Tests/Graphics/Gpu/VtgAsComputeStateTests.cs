using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Engine.Threed.ComputeDraw;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class VtgAsComputeStateTests
    {
        [Test]
        public void GeometryBufferSizingUsesPrimitiveCountPerInstance()
        {
            (int vertexDataSize, int indexDataSize, int indexDataCount) = VtgAsComputeState.CalculateGeometryBufferSizes(
                PrimitiveTopology.TriangleStrip,
                count: 5,
                instanceCount: 3,
                verticesPerPrimitive: 3,
                maxOutputVertices: 3,
                threadsPerInputPrimitive: 1,
                outputSizeInBytesPerInvocation: 16);

            Assert.That(vertexDataSize, Is.EqualTo(432));
            Assert.That(indexDataCount, Is.EqualTo(36));
            Assert.That(indexDataSize, Is.EqualTo(144));
        }

        [Test]
        public void GeometryBufferSizingIncludesRestartSlotsForEachInvocation()
        {
            (int vertexDataSize, int indexDataSize, int indexDataCount) = VtgAsComputeState.CalculateGeometryBufferSizes(
                PrimitiveTopology.Triangles,
                count: 3,
                instanceCount: 1,
                verticesPerPrimitive: 3,
                maxOutputVertices: 6,
                threadsPerInputPrimitive: 4,
                outputSizeInBytesPerInvocation: 16);

            Assert.That(vertexDataSize, Is.EqualTo(384));
            Assert.That(indexDataCount, Is.EqualTo(32));
            Assert.That(indexDataSize, Is.EqualTo(128));
        }
    }
}
