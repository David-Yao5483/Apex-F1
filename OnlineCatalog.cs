using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApexF1;

public sealed record StandingRow(string Position, string Name, string Team, string Points);

internal static class OnlineCatalog
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Uri VideoFeed = new(
        "https://api.bilibili.com/x/polymer/web-space/seasons_series_list?mid=65125803&page_num=1&page_size=10");
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    internal static async Task<IReadOnlyList<SourceLink>> GetNewsAsync(CancellationToken cancellationToken)
        => ParseNews(await DownloadAsync(Catalog.News.Uri, null, cancellationToken));

    internal static async Task<IReadOnlyList<SourceLink>> GetVideosAsync(CancellationToken cancellationToken)
        => ParseVideos(await DownloadAsync(VideoFeed, Catalog.BiliNews.Uri, cancellationToken));

    internal static async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(
        SourceLink source, bool drivers, CancellationToken cancellationToken)
        => ParseStandings(await DownloadAsync(source.Uri, null, cancellationToken), drivers);

    private static async Task<string> DownloadAsync(Uri address, Uri? referrer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ApexF1/1.0");
        request.Headers.Referrer = referrer;
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static IReadOnlyList<SourceLink> ParseNews(string html)
    {
        var articles = Matches(html, """<a\b[^>]*\bhref="(?<path>/en/latest/article/[^"]+)"[^>]*>(?<title>.*?)</a>""")
            .Select(match => new
            {
                Title = PlainText(match.Groups["title"].Value),
                Path = WebUtility.HtmlDecode(match.Groups["path"].Value)
            })
            .Where(article => article.Title.Length > 0)
            .DistinctBy(article => article.Path)
            .Take(12)
            .Select(article => new SourceLink(article.Title, "在应用内阅读原文", "Formula 1",
                new Uri(Catalog.News.Uri, article.Path).AbsoluteUri, "\uE7C3"))
            .ToArray();

        return articles.Length > 0 ? articles : throw new FormatException("F1 article links are missing.");
    }

    internal static IReadOnlyList<SourceLink> ParseVideos(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number ||
            !code.TryGetInt32(out var status) || status != 0 ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("items_lists", out var collections) || collections.ValueKind != JsonValueKind.Object)
            throw new FormatException("Bilibili video collections are unavailable.");

        var videos = new List<(long Published, SourceLink Link)>();
        foreach (var listName in new[] { "seasons_list", "series_list" })
        {
            if (!collections.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var collection in list.EnumerateArray())
            {
                if (collection.ValueKind != JsonValueKind.Object ||
                    !collection.TryGetProperty("archives", out var archives) || archives.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var archive in archives.EnumerateArray())
                {
                    if (archive.ValueKind != JsonValueKind.Object)
                        continue;

                    var id = archive.TryGetProperty("bvid", out var bvid) && bvid.ValueKind == JsonValueKind.String ? bvid.GetString() : null;
                    var title = archive.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title) || id is null || id.Length != 12 ||
                        !id.StartsWith("BV", StringComparison.Ordinal) || !id.All(char.IsAsciiLetterOrDigit))
                        continue;

                    var published = archive.TryGetProperty("pubdate", out var date) && date.ValueKind == JsonValueKind.Number &&
                        date.TryGetInt64(out var seconds)
                        ? seconds : 0;
                    var description = published is > 0 and <= 253402271999
                        ? DateTimeOffset.FromUnixTimeSeconds(published).ToLocalTime().ToString("yyyy-MM-dd")
                        : "在应用内观看";
                    videos.Add((published, new SourceLink(title, description, "F1赛事资讯",
                        $"https://www.bilibili.com/video/{id}/", "\uE714")));
                }
            }
        }

        var latest = videos.OrderByDescending(video => video.Published)
            .DistinctBy(video => video.Link.Address)
            .Take(12)
            .Select(video => video.Link)
            .ToArray();
        return latest.Length > 0 ? latest : throw new FormatException("Bilibili did not return videos.");
    }

    internal static IReadOnlyList<StandingRow> ParseStandings(string html, bool drivers)
    {
        var tables = Matches(html, "<tbody\\b[^>]*>(?<content>.*?)</tbody>");
        foreach (var table in tables)
        {
            var rows = new List<StandingRow>();
            foreach (var row in Matches(table.Groups["content"].Value, "<tr\\b[^>]*>(?<content>.*?)</tr>"))
            {
                var cells = Matches(row.Groups["content"].Value, "<td\\b[^>]*>(?<content>.*?)</td>")
                    .Select(match => match.Groups["content"].Value)
                    .ToArray();
                if (cells.Length != (drivers ? 5 : 3))
                    continue;

                var position = PlainText(cells[0]);
                var points = PlainText(cells[^1]);
                if (!int.TryParse(position, out _) || !decimal.TryParse(points,
                    System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
                    continue;

                var nameCell = drivers
                    ? Regex.Replace(cells[1], """<span\b[^>]*class="(?:[^"]*\s)?md:hidden(?:\s[^"]*)?"[^>]*>[^<]*</span>""",
                        "", RegexOptions.IgnoreCase, MatchTimeout)
                    : cells[1];
                var name = PlainText(nameCell);
                if (name.Length > 0)
                    rows.Add(new StandingRow(position, name, drivers ? PlainText(cells[3]) : "", points));
            }

            if (rows.Count > 0)
                return rows;
        }

        throw new FormatException("F1 standings rows are missing.");
    }

    private static IEnumerable<Match> Matches(string input, string pattern)
        => Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeout).Cast<Match>();

    private static string PlainText(string html)
    {
        var content = Regex.Replace(html, "<[^>]*>", "", RegexOptions.None, MatchTimeout);
        return Regex.Replace(WebUtility.HtmlDecode(content), "\\s+", " ", RegexOptions.None, MatchTimeout).Trim();
    }
}
