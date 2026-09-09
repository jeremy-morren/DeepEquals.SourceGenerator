using Xunit;

// Hash tests set the process-wide seed; the pool seam is process-wide too. Run test classes one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
