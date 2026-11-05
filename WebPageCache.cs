using System.Text.Json;

namespace ApexF1;

internal sealed record WebPageSnapshot(string Title, string Text, WebPageLink[]? Links = null);
internal sealed record WebPageLink(string Title, string Address);

internal static class WebPageCache
{
    internal static bool Supports(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        ((uri.Host == "www.formula1.com" && IsF1ContentPath(uri.AbsolutePath)) ||
         (uri.Host == "www.bilibili.com" && uri.AbsolutePath.StartsWith("/video/", StringComparison.Ordinal)) ||
         (uri.Host == "space.bilibili.com" && (uri.AbsolutePath == "/65125803" || uri.AbsolutePath.StartsWith("/65125803/", StringComparison.Ordinal))));

    private static bool IsF1ContentPath(string path) => path.Split('/', 4) is ["", "en", "latest" or "racing" or "results" or "drivers" or "teams", ..];

    internal static async Task<WebPageSnapshot?> ReadAsync(Uri uri, CancellationToken token = default)
    {
        if (!Supports(uri) || await PageCache.Default.ReadAsync(Key(uri), token) is not { } entry)
            return null;

        try
        {
            return JsonSerializer.Deserialize<WebPageSnapshot>(entry.Text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static Task SaveAsync(Uri uri, WebPageSnapshot page, CancellationToken token = default) =>
        Supports(uri) && (!string.IsNullOrWhiteSpace(page.Text) || page.Links is { Length: > 0 })
            ? PageCache.Default.WriteAsync(Key(uri), JsonSerializer.Serialize(page), token)
            : Task.CompletedTask;

    private static string Key(Uri uri)
    {
        // Keep pagination and collection identity without retaining referral or session parameters.
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => pair.Split('=', 2)[0] is "p" or "pn" or "page" or "sid" or "season_id" or "series_id")
            .Order(StringComparer.Ordinal);
        return "web:" + uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "?" + string.Join('&', query);
    }
}
