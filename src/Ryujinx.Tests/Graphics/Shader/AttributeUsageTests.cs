using NUnit.Framework;
using Ryujinx.Graphics.Shader.Translation;

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
    }
}
