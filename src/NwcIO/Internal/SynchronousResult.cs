namespace NwcIO.Internal;

internal static class SynchronousResult
{
    // Synchronous entry points select synchronous I/O, so every operation must already be complete.
    public static T GetSynchronousResult<T>(this ValueTask<T> operation) =>
        operation.IsCompleted ? operation.GetAwaiter().GetResult() : throw new InvalidOperationException("Unexpected asynchronous operation.");

    public static void GetSynchronousResult(this ValueTask operation)
    {
        if (!operation.IsCompleted)
        {
            throw new InvalidOperationException("Unexpected asynchronous operation.");
        }

        operation.GetAwaiter().GetResult();
    }
}
