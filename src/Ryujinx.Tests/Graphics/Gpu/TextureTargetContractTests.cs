using NUnit.Framework;
using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Image;

namespace Ryujinx.Tests.Graphics.Gpu
{
    public class TextureTargetContractTests
    {
        [Test]
        public void BufferBackedTextureAcceptsOnlyTextureBufferTarget()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: true,
                isPaged: false,
                Target.TextureBuffer,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.True);
            Assert.That(hostTexture, Is.Null);
        }

        [Test]
        public void BufferBackedTextureRejectsOriginalTexture2DTargetWithoutFallingThrough()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: true,
                isPaged: false,
                Target.Texture2D,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.True);
            Assert.That(hostTexture, Is.Null);
        }

        [Test]
        public void OrdinaryTextureReturnsHostTextureForMatchingTarget()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: false,
                isPaged: false,
                Target.Texture2D,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.True);
            Assert.That(hostTexture, Is.Null);
        }

        [Test]
        public void OrdinaryTextureDoesNotClaimUnrelatedTarget()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: false,
                isPaged: false,
                Target.TextureBuffer,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.False);
            Assert.That(hostTexture, Is.Null);
        }

        [Test]
        public void PagedTextureAcceptsOnlyTexture2DArrayTarget()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: false,
                isPaged: true,
                Target.Texture2DArray,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.True);
            Assert.That(hostTexture, Is.Null);
        }

        [Test]
        public void PagedTextureRejectsOriginalTexture2DTargetWithoutCreatingView()
        {
            bool handled = Texture.TryGetDirectTargetTexture(
                Target.Texture2D,
                isBufferBacked: false,
                isPaged: true,
                Target.Texture2D,
                hostTexture: null,
                out ITexture hostTexture);

            Assert.That(handled, Is.True);
            Assert.That(hostTexture, Is.Null);
        }
    }
}
