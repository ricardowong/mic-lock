using Xunit;

// Disable test parallelization because tests manipulate system-wide audio endpoints and global mutexes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
