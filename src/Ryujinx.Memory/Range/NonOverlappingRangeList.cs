using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ryujinx.Memory.Range
{
    /// <summary>
    /// A range list that assumes ranges are non-overlapping, with list items that can be split in two to avoid overlaps.
    /// </summary>
    /// <typeparam name="T">Type of the range.</typeparam>
    public unsafe class NonOverlappingRangeList<T> : RangeListBase<T> where T : class, INonOverlappingRange
    {
        public readonly ReaderWriterLockSlim Lock = new();
        
        /// <summary>
        /// Creates a new non-overlapping range list.
        /// </summary>
        public NonOverlappingRangeList() { }
        
        /// <summary>
        /// Creates a new non-overlapping range list.
        /// </summary>
        /// <param name="backingInitialSize">The initial size of the backing array</param>
        public NonOverlappingRangeList(int backingInitialSize) : base(backingInitialSize) { }
        
        /// <summary>
        /// Adds a new item to the list.
        /// </summary>
        /// <param name="item">The item to be added</param>
        public override void Add(T item)
        {
            int index = BinarySearch(item.Address);
            
            if (index < 0)
            {
                index = ~index;
            }

            RangeItem<T> rangeItem = _rangeItemPool.Allocate().Set(item);
            
            Insert(index, rangeItem);
        }

        /// <summary>
        /// Updates an item's end address on the list. Address must be the same.
        /// </summary>
        /// <param name="item">The item to be updated</param>
        /// <returns>True if the item was located and updated, false otherwise</returns>
        protected override bool Update(T item)
        {
            int index = BinarySearch(item.Address);

            if (index >= 0 && Items[index].Value.Equals(item))
            {
                RangeItem<T> rangeItem = new(item) { Previous = Items[index].Previous, Next = Items[index].Next };
                
                if (index > 0)
                {
                    Items[index - 1].Next = rangeItem;
                }

                if (index < Count - 1)
                {
                    Items[index + 1].Previous = rangeItem;
                }
                
                Items[index] = rangeItem;

                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Updates an item's end address on the list. Address must be the same.
        /// </summary>
        /// <param name="item">The RangeItem to be updated</param>
        /// <returns>True if the item was located and updated, false otherwise</returns>
        protected override bool Update(RangeItem<T> item)
        {
            int index = BinarySearch(item.Address);

            RangeItem<T> rangeItem = new(item.Value) { Previous = item.Previous, Next = item.Next };
                
            if (index > 0)
            {
                Items[index - 1].Next = rangeItem;
            }

            if (index < Count - 1)
            {
                Items[index + 1].Previous = rangeItem;
            }
                
            Items[index] = rangeItem;

            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Insert(int index, RangeItem<T> item)
        {
            Debug.Assert(item.Address != item.EndAddress);
            
            if (Count + 1 > Items.Length)
            {
                Array.Resize(ref Items, Items.Length + BackingGrowthSize);
            }

            if (index >= Count)
            {
                if (index == Count)
                {
                    if (index != 0)
                    {
                        item.Previous = Items[index - 1];
                        Items[index - 1].Next = item;
                    }
                    Items[index] = item;
                    Count++;
                }
            }
            else
            {
                Array.Copy(Items, index, Items, index + 1, Count - index);

                Items[index] = item;
                if (index != 0)
                {
                    item.Previous = Items[index - 1];
                    Items[index - 1].Next = item;
                }
                
                item.Next = Items[index + 1];
                Items[index + 1].Previous = item;
                
                Count++;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveAt(int index)
        {
            _rangeItemPool.Release(Items[index]);
            
            if (index < Count - 1)
            {
                Items[index + 1].Previous = index > 0 ? Items[index - 1] : null;
            }

            if (index > 0)
            {
                Items[index - 1].Next = index < Count - 1 ? Items[index + 1] : null;
            }
            
            if (index < --Count)
            {
                Array.Copy(Items, index + 1, Items, index, Count - index);
            }
        }

        /// <summary>
        /// Removes an item from the list.
        /// </summary>
        /// <param name="item">The item to be removed</param>
        /// <returns>True if the item was removed, or false if it was not found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Remove(T item)
        {
            int index = BinarySearch(item.Address);

            if (index >= 0 && Items[index].Value.Equals(item))
            {
                RemoveAt(index);
                
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes a range of items from the item list
        /// </summary>
        /// <param name="startItem">The first item in the range of items to be removed</param>
        /// <param name="endItem">The last item in the range of items to be removed</param>
        public override void RemoveRange(RangeItem<T> startItem, RangeItem<T> endItem)
        {
            if (startItem is null)
            {
                return;
            }

            if (startItem == endItem)
            {
                Remove(startItem.Value);
                return;
            }
            
            (int startIndex, int endIndex) = BinarySearchEdges(startItem.Address, endItem.EndAddress);
            
            if (endIndex < Count)
            {
                Items[endIndex].Previous = startIndex > 0 ? Items[startIndex - 1] : null;
            }

            if (startIndex > 0)
            {
                Items[startIndex - 1].Next = endIndex < Count ? Items[endIndex] : null;
            }
            
            
            if (endIndex < Count)
            {
                Array.Copy(Items, endIndex, Items, startIndex, Count - endIndex);
            }
            
            Count -= endIndex - startIndex;
        }
        
        /// <summary>
        /// Removes a range of items from the item list
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size of the range</param>
        public void RemoveRange(ulong address, ulong size)
        {
            int startIndex = BinarySearchLeftEdge(address, address + size);
            
            if (startIndex < 0)
            {
                return;
            }
            
            int endIndex = startIndex;
            
            while (Items[endIndex] is not null && Items[endIndex].Address < address + size)
            {
                if (endIndex == Count - 1)
                {
                    break;
                }
                
                endIndex++;
            }
            
            if (endIndex < Count - 1)
            {
                Items[endIndex + 1].Previous = startIndex > 0 ? Items[startIndex - 1] : null;
            }

            if (startIndex > 0)
            {
                Items[startIndex - 1].Next = endIndex < Count - 1 ? Items[endIndex + 1] : null;
            }
            
            
            if (endIndex < Count - 1)
            {
                Array.Copy(Items, endIndex + 1, Items, startIndex, Count - endIndex - 1);
            }
            
            Count -= endIndex - startIndex + 1;
        }

        /// <summary>
        /// Clear all ranges.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Lock.EnterWriteLock();
            Count = 0;
            Lock.ExitWriteLock();
        }
        
        /// <summary>
        /// Finds a list of regions that cover the desired (address, size) range.
        /// If this range starts or ends in the middle of an existing region, it is split and only the relevant part is added.
        /// If there is no matching region, or there is a gap, then new regions are created with the factory.
        /// Regions are added to the list in address ascending order.
        /// </summary>
        /// <param name="list">List to add found regions to</param>
        /// <param name="address">Start address of the search region</param>
        /// <param name="size">Size of the search region</param>
        /// <param name="factory">Factory for creating new ranges</param>
        public void GetOrAddRegions(out List<T> list, ulong address, ulong size, Func<ulong, ulong, T> factory)
        {
            // (regarding the specific case this generalized function is used for)
            // A new region may be split into multiple parts if multiple virtual regions have mapped to it.
            // For instance, while a virtual mapping could cover 0-2 in physical space, the space 0-1 may have already been reserved...
            // So we need to return both the split 0-1 and 1-2 ranges.
            
            Lock.EnterWriteLock();
            (RangeItem<T> first, RangeItem<T> last) = FindOverlapsAsNodes(address, size);
            list = new List<T>();
            
            if (first is null)
            {
                // The region is fully unmapped. Create and add it to the range list.
                T region = factory(address, size);
                list.Add(region);
                Add(region);
            }
            else
            {
                ulong lastAddress = address;
                ulong endAddress = address + size;

                RangeItem<T> current = first;
                while (last is not null && current is not null && current.Address < endAddress)
                {
                    T region = current.Value;
                    if (first == last && region.Address == address && region.Size == size)
                    {
                        // Exact match, no splitting required.
                        list.Add(region);
                        Lock.ExitWriteLock();
                        return;
                    }

                    if (lastAddress < region.Address)
                    {
                        // There is a gap between this region and the last. We need to fill it.
                        T fillRegion = factory(lastAddress, region.Address - lastAddress);
                        list.Add(fillRegion);
                        Add(fillRegion);
                    }

                    if (region.Address < address)
                    {
                        // Split the region around our base address and take the high half.

                        region = Split(region, address);
                    }

                    if (region.EndAddress > address + size)
                    {
                        // Split the region around our end address and take the low half.

                        Split(region, address + size);
                    }

                    list.Add(region);
                    lastAddress = region.EndAddress;
                    current = current.Next;
                }

                if (lastAddress < endAddress)
                {
                    // There is a gap between this region and the end. We need to fill it.
                    T fillRegion = factory(lastAddress, endAddress - lastAddress);
                    list.Add(fillRegion);
                    Add(fillRegion);
                }
            }
            
            Lock.ExitWriteLock();
        }

        /// <summary>
        /// Splits a region around a target point and updates the region list. 
        /// The original region's size is modified, but its address stays the same.
        /// A new region starting from the split address is added to the region list and returned.
        /// </summary>
        /// <param name="region">The region to split</param>
        /// <param name="splitAddress">The address to split with</param>
        /// <returns>The new region (high part)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T Split(T region, ulong splitAddress)
        {
            T newRegion = (T)region.Split(splitAddress);
            Update(region);
            Add(newRegion);
            return newRegion;
        }
        
        /// <summary>
        /// Gets an item on the list overlapping the specified memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <returns>The leftmost overlapping item, or null if none is found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RangeItem<T> FindOverlap(ulong address, ulong size)
        {
            int index = BinarySearchLeftEdge(address, address + size);

            if (index < 0)
            {
                return null;
            }

            return Items[index];
        }
        
        /// <summary>
        /// Gets an item on the list overlapping the specified memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <returns>The overlapping item, or null if none is found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RangeItem<T> FindOverlapFast(ulong address, ulong size)
        {
            int index = BinarySearch(address, address + size);

            if (index < 0)
            {
                return null;
            }

            return Items[index];
        }
        
        /// <summary>
        /// Gets all items on the list overlapping the specified memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <returns>The first and last overlapping items, or null if none are found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (RangeItem<T>, RangeItem<T>) FindOverlapsAsNodes(ulong address, ulong size)
        {
            (int index, int endIndex) = BinarySearchEdges(address, address + size);

            if (index < 0)
            {
                return (null, null);
            }
            
            return (Items[index], Items[endIndex - 1]);
        }
        
        public RangeItem<T>[] FindOverlapsAsArray(ulong address, ulong size, out int length)
        {
            (int index, int endIndex) = BinarySearchEdges(address, address + size);

            RangeItem<T>[] result;
            
            if (index < 0)
            {
                result = null;
                length = 0;
            }
            else
            {
                result = ArrayPool<RangeItem<T>>.Shared.Rent(endIndex - index);
                length = endIndex - index;
                
                Array.Copy(Items, index, result, 0, endIndex - index);
            }
            
            return result;
        }
        
        public Span<RangeItem<T>> FindOverlapsAsSpan(ulong address, ulong size)
        {
            (int index, int endIndex) = BinarySearchEdges(address, address + size);

            Span<RangeItem<T>> result;
            
            if (index < 0)
            {
                result = [];
            }
            else
            {
                result = Items.AsSpan().Slice(index, endIndex - index);
            }
            
            return result;
        }

        public override IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return Items[i].Value;
            }
        }
    }
}
