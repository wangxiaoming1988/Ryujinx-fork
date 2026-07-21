using System;

namespace Ryujinx.Graphics.Gpu.Image
{
    internal static class MacOSGpuSafetyPolicy
    {
        internal const string AllowUnsafePagedTexturesEnvironmentVariable = "RYUJINX_ALLOW_UNSAFE_MACOS_PAGED_TEXTURES";

        public static bool ShouldBlockUnsafePagedTextures()
        {
            return ShouldBlockUnsafePagedTextures(
                OperatingSystem.IsMacOS(),
                Environment.GetEnvironmentVariable(AllowUnsafePagedTexturesEnvironmentVariable));
        }

        internal static bool ShouldBlockUnsafePagedTextures(bool isMacOS, string unsafeOverride)
        {
            return isMacOS && !string.Equals(unsafeOverride, "1", StringComparison.Ordinal);
        }
    }
}
