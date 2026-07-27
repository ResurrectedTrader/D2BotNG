using System.Text;

namespace D2BotNG.Utilities;

/// <summary>
/// Crash-safe file writes. Data is written to a sibling ".tmp", forced all the way to the
/// physical disk (FlushFileBuffers via <see cref="FileStream.Flush(bool)"/>), and only then
/// atomically swapped over the target.
///
/// The flush is the important part: without it, a power-loss or hard crash between the write
/// and the rename can leave the renamed file at its correct length but full of zeros — NTFS
/// commits the new file size to the MFT while the data pages are still only in the OS write
/// cache. That is exactly how a 300 KB all-null characters.json appeared. The frequently
/// rewritten files (characters.json, mule .txt) are the most exposed.
///
/// The swap is retried a few times: another process (antivirus, the search indexer, the game
/// reading d2bs.ini) can briefly hold the destination open and trip a sharing violation.
///
/// Both sync and async forms exist. Prefer async; the sync form is for callers that cannot
/// await, such as IniWriter, which holds a named mutex with thread affinity.
/// </summary>
public static class AtomicFile
{
    private const int ReplaceAttempts = 5;
    private const int ReplaceRetryMs = 50;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Durably and atomically writes UTF-8 (no BOM) text over <paramref name="path"/>.</summary>
    public static Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default) =>
        WriteAllTextAsync(path, contents, Utf8NoBom, cancellationToken);

    /// <summary>Durably and atomically writes text over <paramref name="path"/> in the given encoding.</summary>
    public static Task WriteAllTextAsync(
        string path, string? contents, Encoding encoding, CancellationToken cancellationToken = default) =>
        WriteAllBytesAsync(path, Encode(contents, encoding), cancellationToken);

    /// <summary>Durably and atomically writes bytes over <paramref name="path"/>.</summary>
    public static async Task WriteAllBytesAsync(
        string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        var tempPath = path + ".tmp";
        try
        {
            await WriteDurableAsync(tempPath, contents, cancellationToken);
            await ReplaceWithRetryAsync(tempPath, path, cancellationToken);
        }
        catch
        {
            // Don't leave a stale/partial temp behind for a crashed or aborted write.
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Synchronous <see cref="WriteAllTextAsync(string, string, CancellationToken)"/>, for
    /// callers that cannot await — see the note on thread affinity in the class summary.
    /// </summary>
    public static void WriteAllText(string path, string? contents) =>
        WriteAllText(path, contents, Utf8NoBom);

    /// <summary>Synchronous <see cref="WriteAllTextAsync(string, string, Encoding, CancellationToken)"/>.</summary>
    public static void WriteAllText(string path, string? contents, Encoding encoding) =>
        WriteAllBytes(path, Encode(contents, encoding));

    /// <summary>Synchronous <see cref="WriteAllBytesAsync"/>.</summary>
    public static void WriteAllBytes(string path, byte[] contents)
    {
        var tempPath = path + ".tmp";
        try
        {
            WriteDurable(tempPath, contents);
            ReplaceWithRetry(tempPath, path);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // Null contents writes an empty file, matching File.WriteAllText.
    private static byte[] Encode(string? contents, Encoding encoding) =>
        [.. encoding.GetPreamble(), .. encoding.GetBytes(contents ?? "")];

    private static void WriteDurable(string path, byte[] contents)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteDurableAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(contents, cancellationToken);
        // Flush managed + OS buffers, then force the OS to push the data to the physical
        // disk, so a crash after the swap below can never reveal a zero-filled file.
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Swaps the staged temp over the destination. File.Replace is atomic on NTFS and
    /// preserves the destination's attributes, but requires it to exist.
    /// </summary>
    private static void Replace(string tempPath, string finalPath)
    {
        if (File.Exists(finalPath))
        {
            File.Replace(tempPath, finalPath, null);
        }
        else
        {
            File.Move(tempPath, finalPath);
        }
    }

    private static void ReplaceWithRetry(string tempPath, string finalPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Replace(tempPath, finalPath);
                return;
            }
            catch (Exception ex)
                when ((ex is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
            {
                Thread.Sleep(ReplaceRetryMs * attempt);
            }
        }
    }

    private static async Task ReplaceWithRetryAsync(
        string tempPath, string finalPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Replace(tempPath, finalPath);
                return;
            }
            catch (Exception ex)
                when ((ex is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
            {
                await Task.Delay(ReplaceRetryMs * attempt, cancellationToken);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}
