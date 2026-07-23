using System;

namespace Ryujinx.Graphics.Gpu.Memory
{
    readonly struct IndirectStorageBufferTarget
    {
        public ulong Address { get; }
        public ulong Size { get; }

        public bool IsValid => Size != 0;

        public IndirectStorageBufferTarget(ulong address, ulong size)
        {
            Address = address;
            Size = size;
        }
    }

    static class IndirectStorageBufferResolver
    {
        public static int Resolve(
            ReadOnlySpan<ulong> pointers,
            Span<IndirectStorageBufferTarget> targets,
            Func<ulong, ulong> getMappedSize,
            ulong alignment,
            out bool truncated)
        {
            ArgumentNullException.ThrowIfNull(getMappedSize);

            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment));
            }

            int count = 0;
            truncated = false;

            foreach (ulong pointer in pointers)
            {
                if (pointer == 0 || IsCovered(pointer, targets[..count]))
                {
                    continue;
                }

                ulong mappedSize = getMappedSize(pointer);

                if (mappedSize < sizeof(uint))
                {
                    continue;
                }

                ulong alignedAddress = pointer & ~(alignment - 1);
                ulong misalignment = pointer - alignedAddress;
                ulong alignedSize = mappedSize > ulong.MaxValue - misalignment
                    ? ulong.MaxValue
                    : mappedSize + misalignment;

                if (count == targets.Length)
                {
                    truncated = true;
                    continue;
                }

                targets[count++] = new IndirectStorageBufferTarget(alignedAddress, alignedSize);
            }

            return count;
        }

        private static bool IsCovered(ulong pointer, ReadOnlySpan<IndirectStorageBufferTarget> targets)
        {
            foreach (IndirectStorageBufferTarget target in targets)
            {
                if (pointer >= target.Address && pointer - target.Address < target.Size)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
