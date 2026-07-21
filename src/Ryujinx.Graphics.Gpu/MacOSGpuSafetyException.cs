using System;

namespace Ryujinx.Graphics.Gpu
{
    public sealed class MacOSGpuSafetyException : Exception
    {
        public MacOSGpuSafetyException(string message) : base(message)
        {
        }
    }
}
