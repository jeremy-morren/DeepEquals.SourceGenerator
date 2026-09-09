using System.Buffers;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// The array-pool seam. Production uses <see cref="ArrayPool{T}.Shared"/>; the test assembly replaces it with counting,
/// prefilling, oversized and fail-on-Nth-rent fakes so initialization, clearing and exactly-once return can be asserted.
/// Every owner captures the pool instance it rented from, so a swap never mismatches a return.
/// </summary>
internal static class DeepEqualsPools<T>
{
    internal static ArrayPool<T> Shared = ArrayPool<T>.Shared;
}
