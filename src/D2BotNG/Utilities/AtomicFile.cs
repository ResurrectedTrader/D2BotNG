using System.Text;

namespace D2BotNG.Utilities;

/// <summary>
/// Crash-safe file writes. Data is written to a sibling ".tmp", forced all the way to the
/// physical disk (FlushFileBuffers via <see cref="FileStream.Flush(bool)"/>), and only then
/// atomically renamed over the target.
///
/// The flush is the important part: without it, a power-loss or hard crash between the write
/// and the rename can leave the renamed file at its correct length but full of zeros — NTFS
/// commits the new file size to the MFT while the data pages are still only in the OS write
/// cache. That is exactly how a 300 KB all-null characters.json appeared. The frequently
/// rewritten files (characters.json, mule .txt) are the most exposed.
///
/// The rename is retried a few times: another process (antivirus, the search indexer, the
/// game reading d2bs.ini) can briefly hold the destination open and trip a sharing violation.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Durably and atomically writes UTF-8 (no BOM) text over <paramref name="path"/>.</summary>
    public static Task WriteAllTextAsync(string path, string contents) =>
        WriteAllTextAsync(path, contents, Utf8NoBom);

    /// <summary>Durably and atomically writes text over <paramref name="path"/> in the given encoding.</summary>
    public static async Task WriteAllTextAsync(string path, string contents, Encoding encoding)
    {
        var tempPath = path + ".tmp";
        try
        {
            await WriteDurableAsync(tempPath, encoding, contents);
            await ReplaceWithRetryAsync(tempPath, path);
        }
        catch
        {
            // Don't leave a stale/partial temp behind for a crashed or aborted write.
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Durably writes a single file (no rename) in the given encoding, flushing to physical
    /// disk before returning. For callers that stage their own temp and swap it in themselves
    /// (e.g. IniWriter, which uses <see cref="File.Replace(string, string, string)"/>).
    /// </summary>
    public static void WriteDurable(string path, string contents, Encoding encoding)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
            stream.Write(preamble);
        stream.Write(encoding.GetBytes(contents));
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteDurableAsync(string path, Encoding encoding, string contents)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
            await stream.WriteAsync(preamble);
        await stream.WriteAsync(encoding.GetBytes(contents));
        // Flush managed + OS buffers, then force the OS to push the data to the physical disk
        // so a crash after the rename below can never reveal a zero-filled file.
        stream.Flush(flushToDisk: true);
    }

    private static async Task ReplaceWithRetryAsync(string tempPath, string finalPath)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                return;
            }
            catch (Exception ex)
                when ((ex is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
            {
                await Task.Delay(50 * attempt);
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
