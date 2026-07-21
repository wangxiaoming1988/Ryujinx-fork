using NUnit.Framework;
using Ryujinx.Graphics.Vulkan;

namespace Ryujinx.Tests.Graphics.Vulkan
{
    public class HostImportedBufferUsageTests
    {
        [Test]
        public void HostImportedBufferSupportsTexelBufferViews()
        {
            Assert.That(BufferManager.HostImportedBufferSupportsTexelViews, Is.True);
        }
    }
}
