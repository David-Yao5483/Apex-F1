using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApexF1;

public sealed record TableBadge(Uri? ImageUri, string? Background);

public sealed record StandingRow(string Position, string Name, string Team, string Points, Uri? ImageUri,
    string Nationality, TableBadge? DriverBadge, TableBadge TeamBadge);

public sealed record RaceClassificationRow(string Position, string Number, string Driver, string Team,
    string Laps, string Time, string Points, TableBadge DriverBadge, TableBadge TeamBadge);

internal sealed record RaceSession(string Name, DateTimeOffset Start, DateTimeOffset? End);
internal sealed record RaceCalendar(IReadOnlyList<SourceLink> Races, SourceLink? Next);

internal static class OnlineCatalog
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Uri VideoFeed = new(
        "https://api.bilibili.com/x/polymer/web-space/seasons_series_list?mid=65125803&page_num=1&page_size=10");
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PrefetchFreshness = TimeSpan.FromHours(6);
    private static readonly SemaphoreSlim PrefetchSlots = new(2);
    private const string AbbreviatedDriver = """<span\b[^>]*class="(?:[^"]*\s)?md:hidden(?:\s[^"]*)?"[^>]*>[^<]*</span>""";
    private static readonly Uri TeamPrincipalArticle = new(
        "https://www.formula1.com/en/latest/article/who-are-the-2026-team-principals.2Jrw5LEJbEoX2gM5OtFL8K");

    internal static Task<IReadOnlyList<SourceLink>> GetNewsAsync(CancellationToken token, Action<IReadOnlyList<SourceLink>> cached)
        => LoadAsync(Catalog.News.Uri, ParseNews, token, cached);

    internal static Task<IReadOnlyList<SourceLink>> GetVideosAsync(CancellationToken token, Action<IReadOnlyList<SourceLink>> cached)
        => LoadAsync(VideoFeed, ParseVideos, token, cached, referrer: Catalog.BiliNews.Uri);

    internal static Task<IReadOnlyList<SourceLink>> GetRaceResultsAsync(CancellationToken token, Action<IReadOnlyList<SourceLink>> cached)
        => LoadAsync(Catalog.Results.Uri, ParseRaceResults, token, cached);

    internal static Task<RaceCalendar> GetRaceCalendarAsync(CancellationToken token, Action<RaceCalendar> cached)
        => LoadAsync(Catalog.Calendar.Uri, html => ParseRaceCalendar(html, DateOnly.FromDateTime(DateTime.Now)), token, cached);

    internal static Task<IReadOnlyList<RaceSession>> GetRaceSessionsAsync(
        SourceLink race, CancellationToken token, Action<IReadOnlyList<RaceSession>> cached)
        => LoadAsync(race.Uri, ParseRaceSessions, token, cached);

    internal static Task<IReadOnlyList<RaceClassificationRow>> GetRaceClassificationAsync(
        SourceLink race, CancellationToken token, Action<IReadOnlyList<RaceClassificationRow>> cached)
        => LoadAsync(race.Uri, ParseRaceClassification, token, cached);

    internal static async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(
        SourceLink source, bool drivers, CancellationToken token, Action<IReadOnlyList<StandingRow>> cached)
    {
        if (drivers || !source.Uri.AbsolutePath.StartsWith("/en/results/2026/", StringComparison.Ordinal))
            return await LoadAsync(source.Uri, html => ParseStandings(html, drivers), token, cached);

        var storedPrincipals = await ReadCachedAsync(TeamPrincipalArticle, ParseTeamPrincipals, token);
        var standingsTask = LoadAsync(source.Uri, html => ParseStandings(html, false, storedPrincipals.Data), token, cached);
        var principalsTask = GetTeamPrincipalsAsync(storedPrincipals.Data, token);
        await Task.WhenAll(standingsTask, principalsTask);
        var principals = await principalsTask;
        return (await standingsTask).Select(row => principals?.GetValueOrDefault(TeamKey(row.Name)) is { } principal
            ? row with { Team = principal.Name, ImageUri = principal.Image }
            : row).ToArray();
    }

    private static async Task<IReadOnlyDictionary<string, (string Name, Uri? Image)>?> GetTeamPrincipalsAsync(
        IReadOnlyDictionary<string, (string Name, Uri? Image)>? cached, CancellationToken token)
    {
        try
        {
            return await LoadAsync(TeamPrincipalArticle, ParseAvailableTeamPrincipals, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && IsFeedFailure(ex))
        {
            return cached;
        }
    }

    private static async Task<T> LoadAsync<T>(Uri uri, Func<string, T> parse, CancellationToken token,
        Action<T>? showCached = null, bool preferFreshCache = false, Uri? referrer = null) where T : class
    {
        if (showCached is not null || preferFreshCache)
        {
            var (data, updated) = await ReadCachedAsync(uri, parse, token);
            if (data is not null)
            {
                showCached?.Invoke(data);
                if (preferFreshCache && DateTime.UtcNow - updated < PrefetchFreshness)
                    return data;
            }
        }

        var payload = await DownloadAsync(uri, referrer, token);
        var result = parse(payload);
        await PageCache.Default.WriteAsync(FeedKey(uri), payload, token);
        return result;
    }

    private static async Task<(T? Data, DateTime Updated)> ReadCachedAsync<T>(Uri uri, Func<string, T> parse,
        CancellationToken token) where T : class
    {
        if (await PageCache.Default.ReadAsync(FeedKey(uri), token) is not { } cached)
            return default;

        try
        {
            return (parse(cached.Text), cached.UpdatedUtc);
        }
        catch (Exception ex) when (IsFeedFailure(ex))
        {
            return default;
        }
    }

    private static string FeedKey(Uri uri) => "feed:" + uri.AbsoluteUri;

    internal static bool IsFeedFailure(Exception ex) => ex is HttpRequestException or IOException or FormatException or
        JsonException or RegexMatchTimeoutException or OperationCanceledException;

    internal static Task PrefetchDetailsAsync(string section, IReadOnlyList<SourceLink> links, CancellationToken token) =>
        Parallel.ForEachAsync(links, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, async (link, ct) =>
        {
            await PrefetchSlots.WaitAsync(ct);
            try
            {
                switch (section)
                {
                    case "calendar":
                        await LoadAsync(link.Uri, ParseRaceSessions, ct, preferFreshCache: true);
                        break;
                    case "results":
                        await LoadAsync(link.Uri, ParseRaceClassification, ct, preferFreshCache: true);
                        break;
                    case "news" when await WebPageCache.ReadAsync(link.Uri, ct) is null:
                        var html = await DownloadAsync(link.Uri, null, ct);
                        await WebPageCache.SaveAsync(link.Uri, ParseArticleSnapshot(link, html), ct);
                        break;
                    case "standings":
                        var drivers = link == Catalog.Standings;
                        await LoadAsync(link.Uri, html => ParseStandings(html, drivers), ct, preferFreshCache: true);
                        if (!drivers && link.Uri.AbsolutePath.StartsWith("/en/results/2026/", StringComparison.Ordinal))
                            await LoadAsync(TeamPrincipalArticle, ParseAvailableTeamPrincipals, ct, preferFreshCache: true);
                        break;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && IsFeedFailure(ex))
            {
                // A failed prefetch can be retried when the detail is opened.
            }
            finally
            {
                PrefetchSlots.Release();
            }
        });

    internal static WebPageSnapshot ParseArticleSnapshot(SourceLink link, string html)
    {
        var paragraphs = Matches(html, """<p\b(?=[^>]*\bclass="[^"]*typography-module_body-s-regular)[^>]*>(?<body>.*?)</p>""")
            .Select(match => PlainText(match.Groups["body"].Value)).Where(text => text.Length > 0);
        return new WebPageSnapshot(link.Title, string.Join("\n\n", paragraphs));
    }

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
        var articles = Matches(html, """<li\b[^>]*\bid="article-item-[^"]+"[^>]*>(?<card>.*?)</li>""")
            .Select(match => match.Groups["card"].Value)
            .Select(card => new
            {
                Link = Matches(card, """<a\b[^>]*\bhref="(?<path>/en/latest/article/[^"]+)"[^>]*>(?<title>.*?)</a>""").FirstOrDefault(),
                Image = ReadImage(card)
            })
            .Where(article => article.Link is not null)
            .Select(article => new SourceLink(PlainText(article.Link!.Groups["title"].Value), "在应用内阅读原文",
                "Formula 1", new Uri(Catalog.News.Uri, WebUtility.HtmlDecode(article.Link.Groups["path"].Value)).AbsoluteUri,
                "\uE7C3", ImageUri: article.Image))
            .Where(article => article.Title.Length > 0)
            .DistinctBy(article => article.Address)
            .Take(6)
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
                        $"https://www.bilibili.com/video/{id}/", "\uE714", ImageUri: ReadVideoCover(archive))));
                }
            }
        }

        var latest = videos.OrderByDescending(video => video.Published)
            .DistinctBy(video => video.Link.Address)
            .Take(6)
            .Select(video => video.Link)
            .ToArray();
        return latest.Length > 0 ? latest : throw new FormatException("Bilibili did not return videos.");
    }

    private static Uri? ReadVideoCover(JsonElement archive)
    {
        if (!archive.TryGetProperty("pic", out var picture) || picture.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(picture.GetString(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.Host.EndsWith(".hdslb.com", StringComparison.OrdinalIgnoreCase))
            return null;

        return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
    }

    internal static IReadOnlyList<SourceLink> ParseRaceResults(string html)
    {
        foreach (var table in ReadTables(html))
        {
            var races = new List<(DateOnly Date, SourceLink Link)>();
            foreach (var cells in table)
            {
                if (cells.Length != 6)
                    continue;

                var link = Matches(cells[0], """<a\b[^>]*\bhref="(?<path>/en/results/(?<year>\d{4})/races/[^"]+)"[^>]*>""")
                    .FirstOrDefault();
                var laps = PlainText(cells[4]);
                if (link is null || !int.TryParse(laps, out _))
                    continue;

                var title = PlainText(Regex.Replace(cells[0], "<svg\\b[^>]*>.*?</svg>", "",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase, MatchTimeout));
                var winner = ReadDriverName(cells[2]);
                var team = PlainText(cells[3]);
                var time = PlainText(cells[5]);
                var summary = UiLanguage.Choose(
                    $"冠军 {winner} · {team} · {laps} 圈 · {time}",
                    $"Winner {winner} · {team} · {laps} laps · {time}");
                var dateLabel = PlainText(cells[1]);
                DateOnly.TryParseExact($"{dateLabel} {link.Groups["year"].Value}", "d MMM yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var raceDate);
                races.Add((raceDate, new SourceLink(title, summary, dateLabel,
                    new Uri(Catalog.Results.Uri, WebUtility.HtmlDecode(link.Groups["path"].Value)).AbsoluteUri, "\uE9D9",
                    FlagSvg: ReadRaceFlag(cells[0]))));
            }

            if (races.Count > 0)
                return races.OrderBy(race => race.Date).Select(race => race.Link).ToArray();
        }

        throw new FormatException("F1 race results are missing.");
    }

    internal static IReadOnlyList<RaceClassificationRow> ParseRaceClassification(string html)
    {
        foreach (var table in ReadTables(html))
        {
            var rows = new List<RaceClassificationRow>();
            foreach (var cells in table)
            {
                if (cells.Length != 7)
                    continue;

                var number = PlainText(cells[1]);
                var driver = ReadDriverName(cells[2]);
                if (!int.TryParse(number, out _) || driver.Length == 0)
                    continue;

                rows.Add(new RaceClassificationRow(
                    PlainText(cells[0]), number, driver, PlainText(cells[3]), PlainText(cells[4]),
                    PlainText(cells[5]), PlainText(cells[6]), ReadBadge(cells[2]), ReadBadge(cells[3])));
            }

            if (rows.Count > 0)
                return rows;
        }

        throw new FormatException("F1 race classification rows are missing.");
    }

    internal static RaceCalendar ParseRaceCalendar(string html, DateOnly today)
    {
        var races = new List<SourceLink>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        SourceLink? next = null;
        SourceLink? upcoming = null;
        foreach (var match in Matches(html, """<a\b(?=[^>]*\bclass="group")(?=[^>]*\bhref="(?<path>/en/racing/(?<year>\d{4})/[^"/]+)")[^>]*>(?<body>.*?)</a>"""))
        {
            var body = match.Groups["body"].Value;
            var place = Matches(body, """<p\b[^>]*\bclass="[^"]*group-hover:underline[^"]*"[^>]*>(?<text>.*?)</p>""").FirstOrDefault();
            var title = Matches(body, """>(?<text>FORMULA 1 [^<]+)</span>""").FirstOrDefault();
            var date = Matches(body, """>(?<text>\d{1,2}(?: [A-Za-z]{3})?\s*[-–]\s*(?<end>\d{1,2} [A-Za-z]{3}))</span>""").FirstOrDefault();
            var round = Matches(body, """>(?<text>ROUND \d+|TESTING)</span>""").FirstOrDefault();
            var path = WebUtility.HtmlDecode(match.Groups["path"].Value);
            if (place is null || title is null || date is null || !paths.Add(path))
                continue;

            var officialRound = round?.Groups["text"].Value ?? "";
            var label = officialRound;
            if (!UiLanguage.English)
                label = label == "TESTING" ? "季前测试" : label.Replace("ROUND ", "第 ") + (label.Length > 0 ? " 站" : "");
            var dateLabel = PlainText(date.Groups["text"].Value);
            if (label.Length > 0)
                dateLabel = label + " · " + dateLabel;

            var race = new SourceLink(PlainText(place.Groups["text"].Value), PlainText(title.Groups["text"].Value),
                dateLabel, new Uri(Catalog.Calendar.Uri, path).AbsoluteUri, "\uE787", FlagSvg: ReadRaceFlag(body));
            races.Add(race);

            var endDate = $"{date.Groups["end"].Value} {match.Groups["year"].Value}";
            if (officialRound.StartsWith("ROUND ", StringComparison.Ordinal) &&
                DateOnly.TryParseExact(endDate, "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ends) && ends >= today)
            {
                upcoming ??= race;
                if (body.Contains(">NEXT RACE</span>", StringComparison.OrdinalIgnoreCase))
                    next = race;
            }
        }

        return races.Count > 0 ? new RaceCalendar(races, next ?? upcoming) : throw new FormatException("F1 calendar events are missing.");
    }

    private static string? ReadRaceFlag(string html)
    {
        var svg = Matches(html, """<svg\b(?=[^>]*\bclass="[^"]*\bCountryFlag-module_flag)[^>]*>.*?</svg>""")
            .FirstOrDefault();
        // Direct2D supports the SVG view box and paths, but requires a size other than em.
        return svg?.Value.Replace("width=\"1em\"", "width=\"56\"").Replace("height=\"1em\"", "height=\"56\"");
    }

    internal static IReadOnlyList<RaceSession> ParseRaceSessions(string html)
    {
        foreach (var script in Matches(html, """<script\b[^>]*\btype="application/ld\+json"[^>]*>(?<json>.*?)</script>"""))
        {
            using var document = JsonDocument.Parse(script.Groups["json"].Value);
            var race = document.RootElement;
            if (race.ValueKind != JsonValueKind.Object || !race.TryGetProperty("subEvent", out var events) ||
                events.ValueKind != JsonValueKind.Array)
                continue;

            var sessions = new List<RaceSession>();
            foreach (var session in events.EnumerateArray())
            {
                if (session.ValueKind != JsonValueKind.Object ||
                    !session.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String ||
                    !session.TryGetProperty("startDate", out var startValue) || startValue.ValueKind != JsonValueKind.String ||
                    !startValue.TryGetDateTimeOffset(out var start))
                    continue;

                var name = nameValue.GetString()!.Split(" - ", 2, StringSplitOptions.TrimEntries)[0];
                if (name.Length == 0)
                    continue;

                // The public schedule lists only the start time for the race.
                DateTimeOffset? end = name != "Race" && session.TryGetProperty("endDate", out var endValue) &&
                    endValue.ValueKind == JsonValueKind.String && endValue.TryGetDateTimeOffset(out var finish) && finish > start
                    ? finish : null;
                sessions.Add(new RaceSession(name, start, end));
            }

            if (sessions.Count > 0)
                return sessions.OrderBy(session => session.Start).ToArray();
        }

        throw new FormatException("F1 session times are missing.");
    }

    internal static IReadOnlyDictionary<string, (string Name, Uri? Image)> ParseTeamPrincipals(string html)
    {
        var principals = new Dictionary<string, (string, Uri?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in Matches(html, """<h3\b[^>]*>(?<heading>.*?)</h3>(?<section>.*?)(?=<h[23]\b|</article>|\z)"""))
        {
            var parts = PlainText(heading.Groups["heading"].Value).Split(" – ", 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && ReadImage(heading.Groups["section"].Value) is { } image)
                principals[TeamKey(parts[1])] = (parts[0].Replace("/", " / "), image);
        }

        return principals;
    }

    private static IReadOnlyDictionary<string, (string Name, Uri? Image)> ParseAvailableTeamPrincipals(string html)
    {
        var principals = ParseTeamPrincipals(html);
        return principals.Count > 0 ? principals : throw new FormatException("F1 team principals are missing.");
    }

    internal static IReadOnlyList<StandingRow> ParseStandings(string html, bool drivers,
        IReadOnlyDictionary<string, (string Name, Uri? Image)>? principals = null)
    {
        foreach (var table in ReadTables(html))
        {
            var rows = new List<StandingRow>();
            foreach (var cells in table)
            {
                if (cells.Length != (drivers ? 5 : 3))
                    continue;

                var position = PlainText(cells[0]);
                var points = PlainText(cells[^1]);
                if (!int.TryParse(position, out _) || !decimal.TryParse(points,
                    System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
                    continue;

                var name = drivers ? ReadDriverName(cells[1]) : PlainText(cells[1]);
                if (name.Length == 0)
                    continue;

                if (drivers)
                {
                    var driver = ReadBadge(cells[1]);
                    rows.Add(new StandingRow(position, name, PlainText(cells[3]), points, driver.ImageUri,
                        PlainText(cells[2]), driver, ReadBadge(cells[3])));
                }
                else
                {
                    var principal = principals?.GetValueOrDefault(TeamKey(name));
                    rows.Add(new StandingRow(position, name, principal?.Name ?? "", points, principal?.Image,
                        "", null, ReadBadge(cells[1])));
                }
            }

            if (rows.Count > 0)
                return rows;
        }

        throw new FormatException("F1 standings rows are missing.");
    }

    private static IEnumerable<IEnumerable<string[]>> ReadTables(string html)
        => Matches(html, "<tbody\\b[^>]*>(?<content>.*?)</tbody>").Select(table =>
            Matches(table.Groups["content"].Value, "<tr\\b[^>]*>(?<content>.*?)</tr>").Select(row =>
                Matches(row.Groups["content"].Value, "<td\\b[^>]*>(?<content>.*?)</td>")
                    .Select(cell => cell.Groups["content"].Value).ToArray()));

    private static string ReadDriverName(string cell)
        => PlainText(Regex.Replace(cell, AbbreviatedDriver, "", RegexOptions.IgnoreCase, MatchTimeout));

    private static TableBadge ReadBadge(string cell)
    {
        var color = Matches(cell, """background-color\s*:\s*(?<hex>#[0-9a-f]{6})(?=[";\s])""").FirstOrDefault();
        return new TableBadge(ReadImage(cell), color?.Groups["hex"].Value);
    }

    private static string TeamKey(string name) => name switch
    {
        "Red Bull" => "Red Bull Racing",
        "Haas F1 Team" or "Haas F1" => "Haas",
        _ => name
    };

    private static Uri? ReadImage(string html)
    {
        foreach (var match in Matches(html, """<img\b[^>]*\bsrc="(?<src>[^"]+)"[^>]*>"""))
        {
            if (Uri.TryCreate(WebUtility.HtmlDecode(match.Groups["src"].Value), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.Host == "media.formula1.com")
                return uri;
        }

        return null;
    }

    private static IEnumerable<Match> Matches(string input, string pattern)
        => Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeout).Cast<Match>();

    private static string PlainText(string html)
    {
        var content = Regex.Replace(html, "<[^>]*>", "", RegexOptions.None, MatchTimeout);
        return Regex.Replace(WebUtility.HtmlDecode(content), "\\s+", " ", RegexOptions.None, MatchTimeout).Trim();
    }
}
