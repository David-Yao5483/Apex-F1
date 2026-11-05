using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ApexF1;

public sealed partial class MainWindow
{
    private sealed record CapturedPage(string Address, string Title, string Text, WebPageLink[] Links);

    private Uri? _readerTarget;
    private bool _readerUsesPageCache;

    private const string ReadPublicPage = """
        (() => {
            const f1 = location.hostname === 'www.formula1.com';
            const main = document.querySelector(f1 ? '#maincontent, main, article' : '#v_desc, #app main, main');
            if (!main && f1) return null;
            const description = document.querySelector('meta[name="description"]')?.content || '';
            const article = f1 && location.pathname.startsWith('/en/latest/article/');
            const paragraphs = article && main ? [...main.querySelectorAll('p[class*="typography-module_body-s-regular"]')] : [];
            const title = (main?.querySelector('h1')?.innerText || document.title || '').trim();
            const text = (paragraphs.length ? paragraphs.map(p => p.innerText).join('\n\n') : main?.innerText || description).trim();
            const links = main ? [...main.querySelectorAll('a[href]')].slice(0, 150)
                .map(a => ({ Title: a.innerText.trim().slice(0, 180), Address: a.href }))
                .filter(a => a.Title && a.Address.length <= 2048) : [];
            return { Address: location.href, Title: title.slice(0, 300), Text: text.slice(0, 100000), Links: links };
        })()
        """;

    private async Task CacheReaderPageAsync(WebView2 view, Uri address, Uri? requestedAddress)
    {
        if (!_readerUsesPageCache || !WebPageCache.Supports(address))
            return;

        try
        {
            var json = await view.CoreWebView2.ExecuteScriptAsync(ReadPublicPage);
            if (_webView != view || view.Source != address ||
                JsonSerializer.Deserialize<CapturedPage>(json) is not { } page ||
                !Uri.TryCreate(page.Address, UriKind.Absolute, out var captured) || captured != address)
                return;

            var links = page.Links.Where(link => Uri.TryCreate(link.Address, UriKind.Absolute, out var uri) &&
                WebPageCache.Supports(uri)).DistinctBy(link => link.Address).Take(60).ToArray();
            var snapshot = new WebPageSnapshot(page.Title, page.Text, links);
            await WebPageCache.SaveAsync(address, snapshot);
            if (requestedAddress is not null && requestedAddress != address && requestedAddress.Host == address.Host)
                await WebPageCache.SaveAsync(requestedAddress, snapshot);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or
            ArgumentException or JsonException)
        {
            // Some public pages cannot be captured after a navigation or browser shutdown.
        }
    }

    private async Task<bool> TryShowCachedReaderAsync(Uri address, Func<bool> isCurrent)
    {
        if (!_readerUsesPageCache || !WebPageCache.Supports(address))
            return false;

        var snapshot = await WebPageCache.ReadAsync(address) ?? GetNewsReaderFallback(address);
        if (!isCurrent() || snapshot is null)
            return false;

        ReaderProgress.Visibility = Visibility.Collapsed;
        ReaderError.IsOpen = false;
        ReaderTitle.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? _readerLink?.DisplayTitle ?? address.Host : snapshot.Title;
        ReaderAddress.Text = address.Host;
        CachedReaderNotice.Title = UiLanguage.Choose("正在显示网页缓存", "Showing cached page");
        CachedReaderNotice.Message = UiLanguage.Choose("恢复联网后可刷新网页。", "Refresh the page when connected.");
        CachedReaderText.Text = snapshot.Text;
        CachedReaderText.Visibility = string.IsNullOrWhiteSpace(snapshot.Text) ? Visibility.Collapsed : Visibility.Visible;
        CachedReaderLinks.ItemsSource = (snapshot.Links ?? [])
            .Where(link => Uri.TryCreate(link.Address, UriKind.Absolute, out var uri) && WebPageCache.Supports(uri))
            .Select(link => new SourceLink(link.Title, "", address.Host, link.Address, "\uE7C3")).ToArray();
        WebHost.Visibility = Visibility.Collapsed;
        CachedReader.Visibility = Visibility.Visible;
        CachedReader.ChangeView(null, 0, null, disableAnimation: true);
        return true;
    }

    private WebPageSnapshot? GetNewsReaderFallback(Uri address)
    {
        if (address == Catalog.News.Uri && _news.Count > 0)
            return new WebPageSnapshot(Catalog.News.DisplayTitle, "", _news.Select(ToWebPageLink).ToArray());
        if ((address == Catalog.BiliVideos.Uri || address == Catalog.BiliNews.Uri) && _videos.Count > 0)
            return new WebPageSnapshot(Catalog.BiliNews.DisplayTitle, "", _videos.Select(ToWebPageLink).ToArray());

        var video = _videos.FirstOrDefault(item => item.Uri == address);
        return video is null ? null : new WebPageSnapshot(video.DisplayTitle,
            UiLanguage.Choose($"{video.DisplayProvider} · {video.DisplayDescription}\n\n播放视频需要联网。",
                $"{video.DisplayProvider} · {video.DisplayDescription}\n\nVideo playback requires an internet connection."));
    }

    private static WebPageLink ToWebPageLink(SourceLink link) => new(link.DisplayTitle, link.Address);

    private async void CachedReaderLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { Tag: SourceLink link })
            return;

        if (_webView?.CoreWebView2 is { } core)
            core.Navigate(link.Address);
        else
            await OpenSourceAsync(link);
    }
}
