using NUnit.Framework;
using Ryujinx.Graphics.Shader.Instructions;

namespace Ryujinx.Tests.Graphics.Shader
{
    public class AttributeMapTests
    {
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(4, 2)]
        [TestCase(8, 3)]
        [TestCase(10, 1)]
        [TestCase(unchecked((int)0x80000000), 31)]
        public void ViewportMaskFallbackUsesLowestEnabledViewport(int mask, int expectedIndex)
        {
            Assert.That(AttributeMap.GetViewportIndexFromMask(mask), Is.EqualTo(expectedIndex));
        }
    }
}
