using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace ApexF1;

public sealed partial class MainWindow : Window
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexF1");
    private static readonly string ThemeFile = Path.Combine(DataFolder, "theme.txt");

    private int _featureIndex;
    private WebView2? _webView;
    private SourceLink? _readerLink;
    private Control? _readerOrigin;
    private CancellationTokenSource? _onlineRequest;
    private IReadOnlyList<SourceLink> _news = [];
    private IReadOnlyList<SourceLink> _videos = [];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ApexF1.ico"));
        Root.Loaded += SetInitialWindowBounds;
        RestoreTheme();
        Root.ActualThemeChanged += (_, _) => UpdateTheme();
        UpdateTheme();
        RenderFeature();
        StandingsTabs.SelectedItem = DriverStandingsItem;
        MainTabs.SelectedItem = HomeItem;
        Closed += (_, _) =>
        {
            _onlineRequest?.Cancel();
            ReleaseWebView();
        };
    }

    private void SetInitialWindowBounds(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= SetInitialWindowBounds;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Root.XamlRoot.RasterizationScale;
        var width = Math.Min(workArea.Width, (int)(1240 * scale));
        var height = Math.Min(workArea.Height, (int)(820 * scale));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(workArea.Width, (int)(520 * scale));
            presenter.PreferredMinimumHeight = Math.Min(workArea.Height, (int)(390 * scale));
        }

        AppWindow.MoveAndResize(new(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 700;
        HubContent.Margin = compact ? new Thickness(14, 14, 14, 0) : new Thickness(28, 18, 28, 0);
        Hero.Height = compact ? 296 : 326;
        HeroTitle.FontSize = compact ? 32 : 40;
        HeroArt.Opacity = compact ? 0.55 : 1;
        ExternalLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RestoreTheme()
    {
        try
        {
            Root.RequestedTheme = File.ReadAllText(ThemeFile) switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(ThemeFile, Root.RequestedTheme.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void UpdateTheme()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var label = dark ? "切换到浅色模式" : "切换到深色模式";
        ThemeIcon.Glyph = dark ? "\uE706" : "\uE708";
        ToolTipService.SetToolTip(ThemeButton, label);
        AutomationProperties.SetName(ThemeButton, label);
        if (_webView?.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    private void MainTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string section)
            ShowSection(section);
    }

    private void ShowSection(string section)
    {
        switch (section)
        {
            case "news":
                ShowHub("新闻", "浏览 Formula 1 官网新闻和 F1赛事资讯的近期视频。", NewsGroups());
                OnlineControls.Visibility = Visibility.Visible;
                _ = RefreshOnlineAsync();
                break;
            case "standings":
                ShowHub("积分榜", "查看本赛季 Formula 1 官方积分。", StandingLinks());
                OnlineControls.Visibility = Visibility.Visible;
                StandingsPanel.Visibility = Visibility.Visible;
                _ = RefreshOnlineAsync();
                break;
            case "results":
                ShowHub("赛果", "查看各站正赛、排位、冲刺赛和历年成绩。", [new("官方赛果", [Catalog.Results])]);
                break;
            case "calendar":
                ShowHub("赛历", "查看各站大奖赛、赛道和分节时间。", [new("官方赛历", [Catalog.Calendar])]);
                break;
            case "watch":
                ShowHub("观赛", "央视赛事转播以节目单为准。F1 TV 的直播和回放取决于所在地区及订阅。", [.. Catalog.Watch, .. Catalog.Creators]);
                break;
            default:
                ShowHub("首页", "", Catalog.Home, isHome: true);
                break;
        }
    }

    private void ShowHub(string title, string description, IReadOnlyList<LinkGroup> groups, bool isHome = false)
    {
        _onlineRequest?.Cancel();
        _onlineRequest = null;
        OnlineControls.Visibility = Visibility.Collapsed;
        StandingsPanel.Visibility = Visibility.Collapsed;
        CloseReader();
        PageTitle.Text = title;
        PageDescription.Text = description;
        PageHeading.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
        Hero.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        Groups.ItemsSource = groups;
        Hub.ChangeView(null, 0, null, disableAnimation: true);
    }

    private LinkGroup[] NewsGroups() =>
    [
        new("F1 官网新闻", [.. _news, Catalog.News]),
        new("F1赛事资讯 · 最新视频", [.. _videos, Catalog.BiliNews])
    ];

    private SourceLink SelectedStanding =>
        ReferenceEquals(StandingsTabs.SelectedItem, DriverStandingsItem) ? Catalog.Standings : Catalog.TeamStandings;

    private LinkGroup[] StandingLinks() => [new("数据来源", [SelectedStanding])];

    private void StandingsTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (StandingsPanel.Visibility != Visibility.Visible)
            return;

        StandingRows.ItemsSource = null;
        Groups.ItemsSource = StandingLinks();
        _ = RefreshOnlineAsync();
    }

    private async void RefreshOnline_Click(object sender, RoutedEventArgs e) => await RefreshOnlineAsync();

    private async Task RefreshOnlineAsync()
    {
        _onlineRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _onlineRequest = request;
        OnlineProgress.Visibility = Visibility.Visible;
        F1FeedError.IsOpen = false;
        BiliFeedError.IsOpen = false;

        if (MainTabs.SelectedItem?.Tag as string == "news")
        {
            await Task.WhenAll(
                UpdateFeedAsync(() => OnlineCatalog.GetNewsAsync(request.Token), links => _news = links, F1FeedError, "F1 新闻", request),
                UpdateFeedAsync(() => OnlineCatalog.GetVideosAsync(request.Token), links => _videos = links, BiliFeedError, "B 站视频", request));
        }
        else
        {
            var source = SelectedStanding;
            await UpdateFeedAsync(
                () => OnlineCatalog.GetStandingsAsync(source, source == Catalog.Standings, request.Token),
                rows => StandingRows.ItemsSource = rows, F1FeedError, source.Title, request);
        }

        if (_onlineRequest == request)
        {
            _onlineRequest = null;
            OnlineProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task UpdateFeedAsync<T>(Func<Task<T>> load, Action<T> show, InfoBar error, string label,
        CancellationTokenSource request)
    {
        try
        {
            var result = await load();
            if (_onlineRequest != request)
                return;

            show(result);
            if (MainTabs.SelectedItem?.Tag as string == "news")
                Groups.ItemsSource = NewsGroups();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or FormatException or
            System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException or OperationCanceledException)
        {
            if (_onlineRequest != request || request.IsCancellationRequested)
                return;

            error.Title = $"{label}暂时无法更新";
            error.Message = "可以重试刷新，或打开下方的来源页面。";
            error.IsOpen = true;
        }
    }

    private void RenderFeature()
    {
        var feature = Catalog.Features[_featureIndex];
        HeroLabel.Text = feature.Label;
        HeroTitle.Text = feature.Title;
        HeroDescription.Text = feature.Description;
        HeroAction.Content = feature.Action;
        HeroArt.Source = new SvgImageSource(new Uri($"ms-appx:///Assets/{feature.Art}.svg"));
        FeatureIndex.Text = $"{_featureIndex + 1} / {Catalog.Features.Length}";
    }

    private void PreviousFeature_Click(object sender, RoutedEventArgs e)
    {
        _featureIndex = (_featureIndex + Catalog.Features.Length - 1) % Catalog.Features.Length;
        RenderFeature();
    }

    private void NextFeature_Click(object sender, RoutedEventArgs e)
    {
        _featureIndex = (_featureIndex + 1) % Catalog.Features.Length;
        RenderFeature();
    }

    private async void HeroAction_Click(object sender, RoutedEventArgs e)
    {
        if (Catalog.Features[_featureIndex].Link is SourceLink link)
            await OpenSourceAsync(link);
        else
            MainTabs.SelectedItem = NewsItem;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        SearchBox.Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = Catalog.Search(sender.Text).Take(6).ToArray();
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SourceLink link)
        {
            await OpenSourceAsync(link);
            return;
        }

        var query = args.QueryText.Trim();
        if (query.Length == 0)
            return;

        var matches = Catalog.Search(query).ToArray();
        MainTabs.SelectedItem = null;
        ShowHub("搜索结果", matches.Length == 0 ? $"找不到与“{query}”相关的入口。" : $"“{query}” · {matches.Length} 个入口",
            matches.Length == 0 ? [] : [new("相关入口", matches)]);
    }

    private async void SourceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SourceLink link })
            await OpenSourceAsync(link);
    }

    private async Task OpenSourceAsync(SourceLink link)
    {
        _readerOrigin = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        ReleaseWebView();
        _readerLink = link;
        ReaderTitle.Text = link.Title;
        ReaderAddress.Text = link.Uri.Host;
        ReaderError.IsOpen = false;
        ReaderProgress.Visibility = Visibility.Visible;
        Hub.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Visible;

        var view = new WebView2();
        _webView = view;
        WebHost.Child = view;
        UpdateHistory();
        try
        {
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(DataFolder, "WebView2"), null);
            if (_webView != view)
                return;

            await view.EnsureCoreWebView2Async(environment);
            if (_webView != view)
                return;

            var core = view.CoreWebView2;
            UpdateTheme();
            core.Settings.IsWebMessageEnabled = false;
            core.HistoryChanged += (_, _) =>
            {
                if (_webView == view)
                    UpdateHistory();
            };
            core.ProcessFailed += (_, args) =>
            {
                if (_webView == view && args.ProcessFailedKind is
                    CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                    ShowReaderError("网页进程已退出", "请刷新重试，或在浏览器打开。");
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && GetWebUri(args.Uri) is Uri uri)
                    DispatcherQueue.TryEnqueue(() => { if (_webView == view) view.Source = uri; });
            };
            ulong navigationId = 0;
            view.NavigationStarting += (_, args) =>
            {
                if (GetWebUri(args.Uri) is not Uri uri)
                {
                    args.Cancel = true;
                    return;
                }

                navigationId = args.NavigationId;
                ReaderAddress.Text = uri.Host;
                ReaderError.IsOpen = false;
                ReaderProgress.Visibility = Visibility.Visible;
            };
            view.NavigationCompleted += (_, args) =>
            {
                if (_webView != view || args.NavigationId != navigationId)
                    return;

                ReaderProgress.Visibility = Visibility.Collapsed;
                UpdateHistory();
                if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    ShowReaderError("无法加载网页", "请检查网络后刷新，或在浏览器打开。");
            };
            view.Source = link.Uri;
        }
        catch (Exception)
        {
            if (_webView == view)
                ShowReaderError("无法启动内置网页", "可在浏览器打开，或安装 Microsoft Edge WebView2 Runtime 后刷新。");
        }
    }

    private void ShowReaderError(string title, string message)
    {
        ReaderProgress.Visibility = Visibility.Collapsed;
        ReaderError.Title = title;
        ReaderError.Message = message;
        ReaderError.IsOpen = true;
    }

    private void UpdateHistory()
    {
        BackButton.IsEnabled = _webView?.CanGoBack == true;
        ForwardButton.IsEnabled = _webView?.CanGoForward == true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_webView?.CanGoBack == true)
            _webView.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_webView?.CanGoForward == true)
            _webView.GoForward();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core && !ReaderError.IsOpen)
            core.Reload();
        else if (_readerLink is { } link)
            await OpenSourceAsync(link);
    }

    private async void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var currentLink = _readerLink;
        var uri = _webView?.Source is { } source ? GetWebUri(source.AbsoluteUri) : null;
        uri ??= currentLink?.Uri;
        if (uri is null)
            return;

        try
        {
            if (await Launcher.LaunchUriAsync(uri))
                return;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        if (_readerLink == currentLink)
            ShowReaderError("无法打开浏览器", "请检查 Windows 的默认浏览器设置。");
    }

    private void CloseReader_Click(object sender, RoutedEventArgs e)
    {
        var origin = _readerOrigin;
        CloseReader();
        origin?.Focus(FocusState.Programmatic);
    }

    private void CloseReader()
    {
        ReleaseWebView();
        _readerLink = null;
        _readerOrigin = null;
        Reader.Visibility = Visibility.Collapsed;
        Hub.Visibility = Visibility.Visible;
    }

    private void ReleaseWebView()
    {
        var view = _webView;
        _webView = null;
        if (view is null)
            return;

        WebHost.Child = null;
        view.Close();
    }

    private static Uri? GetWebUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
}
