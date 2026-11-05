using System.Security.Cryptography;
using System.Text;

namespace ApexF1;

internal sealed class PageCache(string folder)
{
    internal sealed record Entry(string Text, DateTime UpdatedUtc);

    internal const int MaxEntryBytes = 8 * 1024 * 1024;
    private const long MaxCacheBytes = 192 * 1024 * 1024;
    private readonly object _pruneLock = new();

    internal static PageCache Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexF1", "PageCache"));

    internal async Task<Entry?> ReadAsync(string key, CancellationToken token = default)
    {
        try
        {
            var path = GetPath(key);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 8192, useAsync: true);
            if (stream.Length is <= 0 or > MaxEntryBytes)
                return null;

            var updated = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            using var reader = new StreamReader(stream);
            return new Entry(await reader.ReadToEndAsync(token).ConfigureAwait(false), updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal async Task WriteAsync(string key, string text, CancellationToken token = default)
    {
        if (text.Length == 0 || Encoding.UTF8.GetByteCount(text) > MaxEntryBytes)
            return;

        var path = GetPath(key);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(temporary, text, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            Prune(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Live content remains usable if the local cache cannot be written.
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private string GetPath(string key) => Path.Combine(folder,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".cache");

    private void Prune(string newest)
    {
        lock (_pruneLock)
        {
            var files = new DirectoryInfo(folder).GetFiles("*.cache").OrderBy(file => file.LastWriteTimeUtc)
                .Select(file => (file.FullName, file.Length)).ToArray();
            var size = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (size <= MaxCacheBytes)
                    break;
                if (file.FullName != newest && TryDelete(file.FullName))
                    size -= file.Length;
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
