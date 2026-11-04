using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace ApexF1;

public sealed partial class MainWindow : Window
{
    private readonly string _webDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexF1", "WebView2");

    private int _featureIndex;
    private WebView2? _webView;
    private SourceLink? _readerLink;
    private Control? _readerOrigin;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ApexF1.ico"));
        Root.Loaded += SetInitialWindowBounds;
        RenderFeature();
        Rail.SelectedItem = HomeItem;
        Closed += (_, _) => ReleaseWebView();
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

    private void Rail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Rail.SelectedItem is ListViewItem { Tag: string section })
            ShowSection(section);
    }

    private void Rail_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (Reader.Visibility == Visibility.Visible && ReferenceEquals(e.ClickedItem, Rail.SelectedItem) &&
            e.ClickedItem is ListViewItem { Tag: string section })
            ShowSection(section);
    }

    private void ShowSection(string section)
    {
        switch (section)
        {
            case "official":
                ShowHub("F1 官网", "从这里进入 Formula 1 官方各栏目，也可在官网首页浏览完整站点。", Catalog.OfficialSite);
                break;
            case "watch":
                ShowHub("观赛", "央视赛事转播以节目单为准。F1 TV 的直播和回放取决于所在地区及订阅。", Catalog.Watch);
                break;
            case "creators":
                ShowHub("创作者", "在哔哩哔哩关注中文赛事资讯、赛后复盘和数据解说。", Catalog.Creators);
                break;
            default:
                ShowHub("首页", "", Catalog.Home, isHome: true);
                break;
        }
    }

    private void ShowHub(string title, string description, IReadOnlyList<LinkGroup> groups, bool isHome = false)
    {
        CloseReader();
        PageTitle.Text = title;
        PageDescription.Text = description;
        PageHeading.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
        Hero.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        Groups.ItemsSource = groups;
        Hub.ChangeView(null, 0, null, disableAnimation: true);
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
            Rail.SelectedItem = CreatorsItem;
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
        Rail.SelectedIndex = -1;
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
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, _webDataFolder, null);
            if (_webView != view)
                return;

            await view.EnsureCoreWebView2Async(environment);
            if (_webView != view)
                return;

            var core = view.CoreWebView2;
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
