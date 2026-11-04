using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ApexF1;

internal static class ImageCache
{
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private const long MaxCacheBytes = 128 * 1024 * 1024;
    private static readonly TimeSpan FreshFor = TimeSpan.FromDays(30);
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexF1", "ImageCache");
    private static readonly HttpClient Client = new();
    private static readonly SemaphoreSlim DownloadSlots = new(4);
    private static readonly ConcurrentDictionary<Uri, Lazy<Task<string?>>> Downloads = new();
    private static readonly object PruneLock = new();

    internal static async Task<string?> GetFileAsync(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            (uri.Host != "media.formula1.com" && !uri.Host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase)))
            return null;

        var work = Downloads.GetOrAdd(uri, key => new Lazy<Task<string?>>(() => DownloadAsync(key)));
        try
        {
            return await work.Value.ConfigureAwait(false);
        }
        finally
        {
            Downloads.TryRemove(new KeyValuePair<Uri, Lazy<Task<string?>>>(uri, work));
        }
    }

    private static async Task<string?> DownloadAsync(Uri uri)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var path = Path.Combine(Folder, key + ".cache");
        if (IsFresh(path))
            return path;

        await DownloadSlots.WaitAsync().ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Folder);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ApexF1/1.0");
            if (uri.Host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase))
                request.Headers.Referrer = new Uri("https://www.bilibili.com/");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxImageBytes ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                return File.Exists(path) ? path : null;

            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + count > MaxImageBytes)
                        return File.Exists(path) ? path : null;
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                }

                if (output.Length == 0)
                    return File.Exists(path) ? path : null;
            }

            File.Move(temporary, path, overwrite: true);
            temporary = null;
            Prune(path);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return File.Exists(path) ? path : null;
        }
        finally
        {
            if (temporary is not null)
                TryDelete(temporary);
            DownloadSlots.Release();
        }
    }

    private static bool IsFresh(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0 && DateTime.UtcNow - file.LastWriteTimeUtc < FreshFor;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Prune(string newest)
    {
        lock (PruneLock)
        {
            var files = new DirectoryInfo(Folder).GetFiles("*.cache").OrderBy(file => file.LastWriteTimeUtc).ToArray();
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
