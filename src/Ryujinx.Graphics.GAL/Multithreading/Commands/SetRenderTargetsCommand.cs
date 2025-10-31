using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;
using System.Buffers;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands
{
    struct SetRenderTargetsCommand : IGALCommand, IGALCommand<SetRenderTargetsCommand>
    {
        public static readonly ArrayPool<ITexture> ArrayPool = ArrayPool<ITexture>.Create(512, 50);
        public readonly CommandType CommandType => CommandType.SetRenderTargets;
        private TableRef<ITexture[]> _colors;
        private TableRef<ITexture> _depthStencil;

        public void Set(TableRef<ITexture[]> colors, TableRef<ITexture> depthStencil)
        {
            _colors = colors;
            _depthStencil = depthStencil;
        }

        public static void Run(ref SetRenderTargetsCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            ITexture[] colors = command._colors.Get(threaded);
            ITexture[] colorsCopy = ArrayPool.Rent(colors.Length);

            for (int i = 0; i < colors.Length; i++)
            {
                colorsCopy[i] = ((ThreadedTexture)colors[i])?.Base;
            }
            
            renderer.Pipeline.SetRenderTargets(colorsCopy, command._depthStencil.GetAs<ThreadedTexture>(threaded)?.Base);
            
            ArrayPool.Return(colorsCopy);
            ArrayPool.Return(colors);
        }
    }
}
