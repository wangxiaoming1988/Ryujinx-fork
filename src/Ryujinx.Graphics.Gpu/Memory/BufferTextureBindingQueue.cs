using Ryujinx.Graphics.GAL;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Gpu.Memory
{
    /// <summary>
    /// Pending buffer texture bindings, coalesced by their host pipeline destination.
    /// </summary>
    internal sealed class BufferTextureBindingQueue
    {
        private readonly List<BufferTextureBinding> _textures = [];
        private readonly List<BufferTextureArrayBinding<ITextureArray>> _textureArrays = [];
        private readonly List<BufferTextureArrayBinding<IImageArray>> _imageArrays = [];

        public IReadOnlyList<BufferTextureBinding> Textures => _textures;
        public IReadOnlyList<BufferTextureArrayBinding<ITextureArray>> TextureArrays => _textureArrays;
        public IReadOnlyList<BufferTextureArrayBinding<IImageArray>> ImageArrays => _imageArrays;

        public int TextureCount => _textures.Count;
        public int TextureArrayCount => _textureArrays.Count;
        public int ImageArrayCount => _imageArrays.Count;

        /// <summary>
        /// Adds a binding or replaces the pending binding for the same pipeline destination.
        /// </summary>
        public void Enqueue(BufferTextureBinding binding)
        {
            for (int index = _textures.Count - 1; index >= 0; index--)
            {
                if (_textures[index].MatchesDestination(binding))
                {
                    _textures[index] = binding;

                    return;
                }
            }

            _textures.Add(binding);
        }

        /// <summary>
        /// Adds a texture-array binding or replaces the pending binding for the same array element.
        /// </summary>
        public void Enqueue(BufferTextureArrayBinding<ITextureArray> binding)
        {
            for (int index = _textureArrays.Count - 1; index >= 0; index--)
            {
                if (_textureArrays[index].MatchesDestination(binding))
                {
                    _textureArrays[index] = binding;

                    return;
                }
            }

            _textureArrays.Add(binding);
        }

        /// <summary>
        /// Adds an image-array binding or replaces the pending binding for the same array element.
        /// </summary>
        public void Enqueue(BufferTextureArrayBinding<IImageArray> binding)
        {
            for (int index = _imageArrays.Count - 1; index >= 0; index--)
            {
                if (_imageArrays[index].MatchesDestination(binding))
                {
                    _imageArrays[index] = binding;

                    return;
                }
            }

            _imageArrays.Add(binding);
        }

        /// <summary>
        /// Removes committed bindings while retaining small list capacities for the next frame.
        /// </summary>
        public void Clear()
        {
            _textures.Clear();
            _textureArrays.Clear();
            _imageArrays.Clear();
        }
    }
}
