using System;

namespace Ryujinx.Graphics.Shader
{
    /// <summary>
    /// Flags that indicate how a buffer will be used in a shader.
    /// </summary>
    [Flags]
    public enum BufferUsageFlags : byte
    {
        None = 0,

        /// <summary>
        /// Buffer is written to.
        /// </summary>
        Write = 1 << 0,

        /// <summary>
        /// Buffer address and size are resolved from another storage buffer at draw time.
        /// </summary>
        Indirect = 1 << 1,
    }
}
