using Microsoft.UI.Windowing;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI.ViewManagement;

namespace ApexF1;

public sealed partial class MainWindow : Window
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexF1");
    private static readonly string ThemeFile = Path.Combine(DataFolder, "theme.txt");
    private static readonly string LanguageFile = Path.Combine(DataFolder, "language.txt");
    private static readonly string WindowFile = Path.Combine(DataFolder, "window.json");

    private sealed record WindowPreferences(double Width, double Height, bool Maximized);

    private WindowPreferences _windowPreferences = new(1240, 820, Maximized: true);

    private WebView2? _webView;
    private SourceLink? _readerLink;
    private Control? _readerOrigin;
    private CancellationTokenSource? _onlineRequest;
    private readonly CancellationTokenSource _prefetchLifetime = new();
    private readonly HashSet<string> _prefetching = [];
    private IReadOnlyList<SourceLink> _news = [];
    private IReadOnlyList<SourceLink> _videos = [];
    private IReadOnlyList<SourceLink> _raceOverview = [];
    private RaceCalendar? _calendarOverview;
    private SourceLink? _raceDetail;
    private string _searchQuery = "";
    private bool _changingLanguage;
    private bool _backAvailable;
    private Storyboard? _backTransition;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ApexF1.ico"));
        Root.Loaded += SetInitialWindowBounds;
        RestoreTheme();
        var savedLanguage = ReadPreference(LanguageFile);
        UiLanguage.English = UiLanguage.UseEnglish(savedLanguage, CultureInfo.CurrentUICulture);
        if (savedLanguage is not ("en" or "zh-CN"))
            SavePreference(LanguageFile, UiLanguage.Choose("zh-CN", "en"));
        ApplyLanguage();
        Root.ActualThemeChanged += (_, _) => UpdateTheme();
        UpdateTheme();
        StandingsTabs.SelectedItem = DriverStandingsItem;
        MainTabs.SelectedItem = NewsItem;
        Closed += (_, _) =>
        {
            _onlineRequest?.Cancel();
            _prefetchLifetime.Cancel();
            _prefetchLifetime.Dispose();
            ReleaseWebView();
        };
    }

    private void SetInitialWindowBounds(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= SetInitialWindowBounds;
        if (ReadPreference(WindowFile) is { } json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var settings = document.RootElement;
                if (settings.Deserialize<WindowPreferences>() is { } saved &&
                    double.IsFinite(saved.Width) && saved.Width > 0 &&
                    double.IsFinite(saved.Height) && saved.Height > 0)
                {
                    var wasFullScreen = settings.TryGetProperty("FullScreen", out var mode) &&
                        mode.ValueKind == JsonValueKind.True;
                    _windowPreferences = saved with { Maximized = saved.Maximized || wasFullScreen };
                }
            }
            catch (JsonException)
            {
                // An incomplete settings file can be replaced on the next close.
            }
        }

        ApplyNormalWindowSize();
        if (_windowPreferences.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.Maximize();

        AppWindow.Changed += (_, _) => RememberWindowSize();
        AppWindow.Closing += (_, _) =>
        {
            RememberWindowSize();
            SavePreference(WindowFile, JsonSerializer.Serialize(_windowPreferences));
        };
    }

    private void ApplyNormalWindowSize()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scale = Root.XamlRoot.RasterizationScale;
        var minWidth = Math.Min(workArea.Width, (int)(520 * scale));
        var minHeight = Math.Min(workArea.Height, (int)(390 * scale));
        var width = (int)Math.Clamp(Math.Round(_windowPreferences.Width * scale), minWidth, workArea.Width);
        var height = (int)Math.Clamp(Math.Round(_windowPreferences.Height * scale), minHeight, workArea.Height);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minWidth;
            presenter.PreferredMinimumHeight = minHeight;
        }

        AppWindow.MoveAndResize(new(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2, width, height));
    }

    private void RememberWindowSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            _windowPreferences = _windowPreferences with { Maximized = true };
        }
        else if (presenter.State == OverlappedPresenterState.Restored)
        {
            var scale = Root.XamlRoot.RasterizationScale;
            var size = AppWindow.Size;
            _windowPreferences = new(size.Width / scale, size.Height / scale, Maximized: false);
        }
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 700;
        HubContent.Margin = compact ? new Thickness(14, 14, 14, 0) : new Thickness(28, 18, 28, 0);
        ExternalLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RestoreTheme()
    {
        Root.RequestedTheme = ReadPreference(ThemeFile) switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    private static string? ReadPreference(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SavePreference(string path, string value)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(path, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        SavePreference(ThemeFile, Root.RequestedTheme.ToString());
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        UiLanguage.English = !UiLanguage.English;
        SavePreference(LanguageFile, UiLanguage.Choose("zh-CN", "en"));
        ApplyLanguage();
        UpdateTheme();
        SearchBox.ItemsSource = null;
        _changingLanguage = true;
        try
        {
            if (MainTabs.SelectedItem?.Tag is string section)
                ShowSection(section);
            else
                ShowSearchResults(_searchQuery);
        }
        finally
        {
            _changingLanguage = false;
        }
    }

    private void ApplyLanguage()
    {
        Root.Language = UiLanguage.Choose("zh-CN", "en-US");
        LanguageButton.Content = UiLanguage.Choose("EN", "中文");
        Describe(LanguageButton, "切换到英文", "Switch to Chinese");
        var search = UiLanguage.Choose("搜索栏目、频道或创作者", "Search sections, channels or creators");
        SearchBox.PlaceholderText = search;
        AutomationProperties.SetName(SearchBox, search);
        AutomationProperties.SetName(MainTabs, UiLanguage.Choose("主导航", "Main navigation"));
        AutomationProperties.SetName(StandingsTabs, UiLanguage.Choose("积分榜类别", "Standings categories"));
        foreach (var item in MainTabs.Items)
        {
            item.Text = UiLanguage.Translate(item.Tag switch
            {
                "news" => "新闻",
                "standings" => "积分榜",
                "results" => "赛果",
                "calendar" => "赛历",
                "watch" => "观赛",
                _ => "新闻"
            });
        }

        NewsHeading.Text = UiLanguage.Translate("F1 官网新闻");
        ReadMoreButton.Content = UiLanguage.Choose("阅读更多", "Read more");
        VideosHeading.Text = UiLanguage.Translate("F1赛事资讯 · 最新视频");
        WatchMoreButton.Content = UiLanguage.Choose("观看更多", "Watch more");
        Describe(RefreshButton, "刷新当前页面", "Refresh current page");
        DriverStandingsItem.Text = UiLanguage.Choose("车手积分榜", "Drivers");
        TeamStandingsItem.Text = UiLanguage.Choose("车队积分榜", "Teams");
        ExternalLabel.Text = UiLanguage.Choose("在浏览器打开", "Open in browser");
        Describe(ExternalButton, "在浏览器打开", "Open in browser");
        UpdateBackNavigation();
        if (_readerLink is { } link)
            ReaderTitle.Text = link.DisplayTitle;
    }

    private static void Describe(Button button, string chinese, string english)
    {
        var description = UiLanguage.Choose(chinese, english);
        AutomationProperties.SetName(button, description);
        ToolTipService.SetToolTip(button, description);
    }

    private void UpdateTheme()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        ThemeIcon.Glyph = dark ? "\uE706" : "\uE708";
        Describe(ThemeButton, dark ? "切换到浅色模式" : "切换到深色模式",
            dark ? "Switch to light mode" : "Switch to dark mode");
        if (_webView?.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    private void MainTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _raceDetail = null;
        if (sender.SelectedItem?.Tag is string section)
            ShowSection(section);
    }

    private void ShowSection(string section)
    {
        switch (section)
        {
            case "news":
                ShowHub("新闻", "浏览 Formula 1 官网新闻和 F1赛事资讯的近期视频。", []);
                NewsCards.ItemsSource = _news;
                VideoCards.ItemsSource = _videos;
                NewsPanel.Visibility = Visibility.Visible;
                OnlineControls.Visibility = Visibility.Visible;
                break;
            case "standings":
                ShowHub("积分榜", "查看本赛季 Formula 1 官方积分。", StandingLinks());
                OnlineControls.Visibility = Visibility.Visible;
                ResultsTablePanel.Visibility = Visibility.Visible;
                StandingsTabs.Visibility = Visibility.Visible;
                ShowStandingTable([]);
                break;
            case "results" when _raceDetail is { } race:
                ShowHub(race.Title, race.Provider, [new("官方赛果", [race])]);
                ResultsTablePanel.Visibility = Visibility.Visible;
                ResultsTable.ShowRace([]);
                OnlineControls.Visibility = Visibility.Visible;
                break;
            case "results":
                ShowHub("赛果", "查看本赛季各站的正赛冠军、车队、圈数和完赛时间。", [new("官方赛果", [Catalog.Results])]);
                ShowRaceResults(_raceOverview);
                RaceRows.Visibility = Visibility.Visible;
                OnlineControls.Visibility = Visibility.Visible;
                break;
            case "calendar" when _raceDetail is { } race:
                ShowHub(race.Title, race.Description, [new("官方赛历", [race])]);
                ResultsTablePanel.Visibility = Visibility.Visible;
                ResultsTable.ShowSchedule([]);
                OnlineControls.Visibility = Visibility.Visible;
                break;
            case "calendar":
                ShowHub("赛历", "查看各站大奖赛、赛道和分节时间。", [new("官方赛历", [Catalog.Calendar])]);
                ShowRaceCalendar(_calendarOverview);
                RaceRows.Visibility = Visibility.Visible;
                OnlineControls.Visibility = Visibility.Visible;
                break;
            case "watch":
                ShowHub("观赛", "央视赛事转播以节目单为准。F1 TV 的直播和回放取决于所在地区及订阅。", [.. Catalog.Watch, .. Catalog.Creators]);
                break;
        }

        UpdateRefreshButton();
        if (OnlineControls.Visibility == Visibility.Visible)
            _ = RefreshOnlineAsync();
    }

    private void ShowHub(string title, string description, IReadOnlyList<LinkGroup> groups)
    {
        _onlineRequest?.Cancel();
        _onlineRequest = null;
        OnlineControls.Visibility = Visibility.Collapsed;
        NewsPanel.Visibility = Visibility.Collapsed;
        ResultsTablePanel.Visibility = Visibility.Collapsed;
        StandingsTabs.Visibility = Visibility.Collapsed;
        RaceRows.Visibility = Visibility.Collapsed;
        RaceRows.ItemsSource = null;
        FeaturedRacePanel.Visibility = Visibility.Collapsed;
        FeaturedRaceRow.ItemsSource = null;
        if (!_changingLanguage)
            CloseReader();
        PageTitle.Text = UiLanguage.Translate(title);
        RaceFlag.SetSvg(PageRaceFlag, _raceDetail?.FlagSvg);
        PageDescription.Text = UiLanguage.Translate(description);
        Groups.ItemsSource = groups.ToArray();
        UpdateRefreshButton();
        UpdateBackNavigation();
        if (!_changingLanguage)
            Hub.ChangeView(null, 0, null, disableAnimation: true);
    }

    private async void ReadMore_Click(object sender, RoutedEventArgs e) => await OpenSourceAsync(Catalog.News);

    private async void WatchMore_Click(object sender, RoutedEventArgs e) => await OpenSourceAsync(Catalog.BiliVideos);

    private SourceLink SelectedStanding =>
        ReferenceEquals(StandingsTabs.SelectedItem, DriverStandingsItem) ? Catalog.Standings : Catalog.TeamStandings;

    private LinkGroup[] StandingLinks() => [new("数据来源", [SelectedStanding])];

    private void StandingsTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (StandingsTabs.Visibility != Visibility.Visible)
            return;

        ShowStandingTable([]);
        Groups.ItemsSource = StandingLinks();
        _ = RefreshOnlineAsync();
    }

    private void ShowStandingTable(IReadOnlyList<StandingRow> rows)
    {
        if (SelectedStanding == Catalog.Standings)
            ResultsTable.ShowDrivers(rows);
        else
            ResultsTable.ShowTeams(rows);
    }

    private void ShowRaceCalendar(RaceCalendar? calendar)
    {
        _calendarOverview = calendar;
        ShowRaceOverview(calendar?.Races, calendar?.Next, UiLanguage.Choose("下一站", "Next race"));
    }

    private void ShowRaceResults(IReadOnlyList<SourceLink> races)
    {
        _raceOverview = races;
        ShowRaceOverview(races, races.LastOrDefault(), UiLanguage.Choose("上一站", "Previous race"));
    }

    private void ShowRaceOverview(IReadOnlyList<SourceLink>? races, SourceLink? featured, string heading)
    {
        RaceRows.ItemsSource = races;
        FeaturedRaceHeading.Text = heading;
        FeaturedRaceRow.ItemsSource = featured is null ? null : new[] { featured };
        FeaturedRacePanel.Visibility = featured is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RefreshOnlineAsync()
    {
        _onlineRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _onlineRequest = request;
        OnlineProgress.Visibility = Visibility.Visible;
        F1FeedError.IsOpen = false;
        BiliFeedError.IsOpen = false;

        switch (MainTabs.SelectedItem?.Tag as string)
        {
            case "news":
                await Task.WhenAll(
                    UpdateFeedAsync<IReadOnlyList<SourceLink>>(cached => OnlineCatalog.GetNewsAsync(request.Token, cached),
                        links => NewsCards.ItemsSource = _news = links,
                        F1FeedError, UiLanguage.Translate("F1 官网新闻"), request, links => PrefetchDetails("news", links)),
                    UpdateFeedAsync<IReadOnlyList<SourceLink>>(cached => OnlineCatalog.GetVideosAsync(request.Token, cached),
                        links => VideoCards.ItemsSource = _videos = links,
                        BiliFeedError, UiLanguage.Choose("B 站视频", "Bilibili videos"), request));
                break;
            case "standings":
                var source = SelectedStanding;
                await UpdateFeedAsync<IReadOnlyList<StandingRow>>(
                    cached => OnlineCatalog.GetStandingsAsync(source, source == Catalog.Standings, request.Token, cached),
                    ShowStandingTable, F1FeedError, source.DisplayTitle, request,
                    _ => PrefetchDetails("standings", [Catalog.Standings, Catalog.TeamStandings]));
                break;
            case "results" when _raceDetail is { } race:
                await UpdateFeedAsync<IReadOnlyList<RaceClassificationRow>>(
                    cached => OnlineCatalog.GetRaceClassificationAsync(race, request.Token, cached),
                    ResultsTable.ShowRace, F1FeedError, race.DisplayTitle, request);
                break;
            case "results":
                await UpdateFeedAsync<IReadOnlyList<SourceLink>>(cached => OnlineCatalog.GetRaceResultsAsync(request.Token, cached),
                    ShowRaceResults, F1FeedError, Catalog.Results.DisplayTitle, request,
                    links => PrefetchDetails("results", links.Reverse().ToArray()));
                break;
            case "calendar" when _raceDetail is { } schedule:
                await UpdateFeedAsync<IReadOnlyList<RaceSession>>(
                    cached => OnlineCatalog.GetRaceSessionsAsync(schedule, request.Token, cached),
                    ResultsTable.ShowSchedule, F1FeedError, schedule.DisplayTitle, request);
                break;
            case "calendar":
                await UpdateFeedAsync<RaceCalendar>(cached => OnlineCatalog.GetRaceCalendarAsync(request.Token, cached),
                    ShowRaceCalendar, F1FeedError, Catalog.Calendar.DisplayTitle, request,
                    calendar => PrefetchDetails("calendar", calendar.Races));
                break;
        }

        if (_onlineRequest == request)
        {
            _onlineRequest = null;
            OnlineProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task UpdateFeedAsync<T>(Func<Action<T>, Task<T>> load, Action<T> show, InfoBar error, string label,
        CancellationTokenSource request, Action<T>? onUpdated = null)
    {
        var showedCache = false;
        try
        {
            var result = await load(cached =>
            {
                if (_onlineRequest != request)
                    return;

                show(cached);
                showedCache = true;
            });
            if (_onlineRequest != request)
                return;

            show(result);
            onUpdated?.Invoke(result);
        }
        catch (Exception ex) when (OnlineCatalog.IsFeedFailure(ex))
        {
            if (_onlineRequest != request || request.IsCancellationRequested)
                return;

            error.Severity = showedCache ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
            error.Title = showedCache
                ? UiLanguage.Choose($"正在显示{label}的缓存", $"Showing cached {label}")
                : UiLanguage.Choose($"{label}暂时无法更新", $"Could not refresh {label}");
            error.Message = showedCache
                ? UiLanguage.Choose("恢复联网后可刷新以获取最新内容。", "Refresh when connected to get the latest content.")
                : UiLanguage.Choose("可以重试刷新，或打开下方的来源页面。", "Try again or open the source page below.");
            error.IsOpen = true;
        }
    }

    private async void PrefetchDetails(string section, IReadOnlyList<SourceLink> links)
    {
        if (!_prefetching.Add(section))
            return;

        var token = _prefetchLifetime.Token;
        try
        {
            await OnlineCatalog.PrefetchDetailsAsync(section, links, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            _prefetching.Remove(section);
        }
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

        _searchQuery = query;
        MainTabs.SelectedItem = null;
        ShowSearchResults(query);
    }

    private void ShowSearchResults(string query)
    {
        var matches = Catalog.Search(query).ToArray();
        var description = matches.Length == 0
            ? UiLanguage.Choose($"找不到与“{query}”相关的入口。", $"No links match “{query}”.")
            : UiLanguage.Choose($"“{query}” · {matches.Length} 个入口", $"“{query}” · {matches.Length} links");
        ShowHub("搜索结果", description,
            matches.Length == 0 ? [] : [new("相关入口", matches)]);
    }

    private async void SourceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SourceLink link })
            await OpenSourceAsync(link);
    }

    private void RaceRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SourceLink race } || MainTabs.SelectedItem?.Tag is not string section)
            return;

        _raceDetail = race;
        ShowSection(section);
        TitleBackButton.Focus(FocusState.Programmatic);
    }

    private void TitleBack_Click(object sender, RoutedEventArgs e)
    {
        if (_webView?.CanGoBack == true)
        {
            _webView.GoBack();
        }
        else if (_readerLink is not null)
        {
            var origin = _readerOrigin;
            CloseReader();
            UpdateBackNavigation();
            origin?.Focus(FocusState.Programmatic);
        }
        else if (_raceDetail is not null && MainTabs.SelectedItem?.Tag is string section)
        {
            _raceDetail = null;
            ShowSection(section);
            MainTabs.SelectedItem?.Focus(FocusState.Programmatic);
        }
    }

    private async Task OpenSourceAsync(SourceLink link, Uri? target = null)
    {
        if (_readerLink is null)
            _readerOrigin = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        ReleaseWebView();
        _readerLink = link;
        _readerTarget = target ?? link.Uri;
        _readerUsesPageCache = MainTabs.SelectedItem?.Tag is "news" or "standings" or "results" or "calendar";
        ReaderTitle.Text = link.DisplayTitle;
        ReaderAddress.Text = _readerTarget.Host;
        ReaderError.IsOpen = false;
        ReaderProgress.Visibility = Visibility.Visible;
        CachedReader.Visibility = Visibility.Collapsed;
        WebHost.Visibility = Visibility.Visible;
        Hub.Visibility = Visibility.Collapsed;
        Reader.Visibility = Visibility.Visible;
        UpdateRefreshButton();

        var view = new WebView2();
        _webView = view;
        WebHost.Child = view;
        UpdateBackNavigation();
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
                    UpdateBackNavigation();
            };
            core.ProcessFailed += (_, args) =>
            {
                if (_webView == view && args.ProcessFailedKind is
                    CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                    ShowReaderError(UiLanguage.Choose("网页进程已退出", "The web process closed"),
                        UiLanguage.Choose("请刷新重试，或在浏览器打开。", "Reload or open the page in your browser."));
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && GetWebUri(args.Uri) is Uri uri)
                    DispatcherQueue.TryEnqueue(() => { if (_webView == view) view.Source = uri; });
            };
            ulong navigationId = 0;
            Uri? requestedAddress = null;
            view.NavigationStarting += (_, args) =>
            {
                if (GetWebUri(args.Uri) is not Uri uri)
                {
                    args.Cancel = true;
                    return;
                }

                if (navigationId != args.NavigationId)
                    requestedAddress = uri;
                navigationId = args.NavigationId;
                _readerTarget = uri;
                ReaderAddress.Text = uri.Host;
                ReaderError.IsOpen = false;
                ReaderProgress.Visibility = Visibility.Visible;
                CachedReader.Visibility = Visibility.Collapsed;
                WebHost.Visibility = Visibility.Visible;
            };
            view.NavigationCompleted += async (_, args) =>
            {
                if (_webView != view || args.NavigationId != navigationId)
                    return;

                ReaderProgress.Visibility = Visibility.Collapsed;
                UpdateBackNavigation();
                if (args.IsSuccess && view.Source is { } address)
                {
                    if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
                        ReaderTitle.Text = core.DocumentTitle;
                    await CacheReaderPageAsync(view, address, requestedAddress);
                }
                else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    bool IsCurrent() => _webView == view && args.NavigationId == navigationId;
                    if (await TryShowCachedReaderAsync(_readerTarget ?? link.Uri, IsCurrent) || !IsCurrent())
                        return;

                    ShowReaderError(UiLanguage.Choose("无法加载网页", "Could not load the page"),
                        UiLanguage.Choose("请检查网络后刷新，或在浏览器打开。", "Check your connection, then reload or open in your browser."));
                }
            };
            view.Source = _readerTarget;
        }
        catch (Exception)
        {
            if (_webView != view || await TryShowCachedReaderAsync(_readerTarget ?? link.Uri, () => _webView == view) || _webView != view)
                return;

            ShowReaderError(UiLanguage.Choose("无法启动内置网页", "Could not open the in-app browser"),
                UiLanguage.Choose("可在浏览器打开，或安装 Microsoft Edge WebView2 Runtime 后刷新。",
                    "Open in your browser, or install the Microsoft Edge WebView2 Runtime and reload."));
        }
    }

    private void ShowReaderError(string title, string message)
    {
        ReaderProgress.Visibility = Visibility.Collapsed;
        ReaderError.Title = title;
        ReaderError.Message = message;
        ReaderError.IsOpen = true;
    }

    // Detail views share this one title-bar action. History updates can change its destination without restarting the animation.
    private void UpdateBackNavigation()
    {
        var canGoBack = _readerLink is not null || _raceDetail is not null;
        if (_webView?.CanGoBack == true)
            Describe(TitleBackButton, "网页后退", "Go back one web page");
        else if (_readerLink is not null)
            Describe(TitleBackButton, "返回上一页", "Back to the previous app page");
        else if (MainTabs.SelectedItem?.Tag as string == "calendar")
            Describe(TitleBackButton, "返回赛历", "Back to calendar");
        else
            Describe(TitleBackButton, "返回分站赛果", "Back to race results");

        if (_backAvailable == canGoBack)
            return;

        _backAvailable = canGoBack;
        var startWidth = TitleBackSlot.ActualWidth;
        var endWidth = canGoBack ? TitleBackButton.Width : 0;
        var endOpacity = canGoBack ? 1d : 0d;
        _backTransition?.Stop();
        _backTransition = null;

        TitleBackButton.IsHitTestVisible = canGoBack;
        TitleBackButton.IsTabStop = canGoBack;
        TitleBackSlot.Width = endWidth;
        TitleBackButton.Opacity = endOpacity;
        if (!new UISettings().AnimationsEnabled)
        {
            TitleBackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        TitleBackButton.Visibility = Visibility.Visible;
        var transition = new Storyboard();
        AddBackAnimation(transition, TitleBackSlot, nameof(TitleBackSlot.Width), startWidth, endWidth);
        AddBackAnimation(transition, TitleBackButton, nameof(TitleBackButton.Opacity), startWidth / TitleBackButton.Width, endOpacity);
        transition.Completed += (_, _) =>
        {
            if (_backTransition != transition)
                return;

            _backTransition = null;
            transition.Stop();
            if (!canGoBack)
                TitleBackButton.Visibility = Visibility.Collapsed;
        };
        _backTransition = transition;
        transition.Begin();
    }

    private static void AddBackAnimation(Storyboard transition, UIElement target, string property, double from, double to)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(240)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        transition.Children.Add(animation);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core && !ReaderError.IsOpen)
            core.Reload();
        else if (_readerLink is { } link)
            await OpenSourceAsync(link, _readerTarget);
        else if (OnlineControls.Visibility == Visibility.Visible)
            await RefreshOnlineAsync();
    }

    private void UpdateRefreshButton() =>
        RefreshButton.IsEnabled = _readerLink is not null || OnlineControls.Visibility == Visibility.Visible;

    private async void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var currentLink = _readerLink;
        var uri = CachedReader.Visibility == Visibility.Visible ? _readerTarget
            : _webView?.Source is { } source ? GetWebUri(source.AbsoluteUri) : null;
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
            ShowReaderError(UiLanguage.Choose("无法打开浏览器", "Could not open the browser"),
                UiLanguage.Choose("请检查 Windows 的默认浏览器设置。", "Check your Windows default browser settings."));
    }

    private void CloseReader()
    {
        ReleaseWebView();
        _readerLink = null;
        _readerOrigin = null;
        _readerTarget = null;
        _readerUsesPageCache = false;
        CachedReader.Visibility = Visibility.Collapsed;
        CachedReaderLinks.ItemsSource = null;
        CachedReaderText.Text = "";
        Reader.Visibility = Visibility.Collapsed;
        Hub.Visibility = Visibility.Visible;
        UpdateRefreshButton();
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
