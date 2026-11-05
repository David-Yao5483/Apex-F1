using System.Globalization;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ApexF1;

public sealed class NativeResultsTable : UserControl
{
    private sealed record Cell(string Text, TableBadge? Badge = null, string? Principal = null,
        Uri? PrincipalImage = null, double BadgeInset = 0, bool IsDriverPortrait = false);

    private static readonly string[] DriverHeaders = ["POS.", "DRIVER", "NATIONALITY", "TEAM", "PTS."];
    private static readonly double[] DriverWidths = [12.25, 33.38, 20.34, 26.03, 8];
    private static readonly string[] TeamHeaders = ["POS.", "TEAM", "PTS."];
    private static readonly double[] TeamWidths = [26.5, 63.5, 10];
    private static readonly string[] RaceHeaders = ["POS.", "NO.", "DRIVER", "TEAM", "LAPS", "TIME / RETIRED", "PTS."];
    private static readonly double[] RaceWidths = [9.72, 8.99, 26.4, 22.82, 9.97, 16.1, 6];
    private static readonly FontFamily TableFont = new("ms-appx:///Assets/TitilliumWeb-SemiBold.ttf#Titillium Web SemiBold");

    // Face centers measured in the official 64px-wide 2026 portrait images.
    private static readonly Dictionary<string, (double X, double Y)> DriverFaceCenters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alealb01"] = (31, 20), ["andant01"] = (29, 18), ["arvlin01"] = (31, 20),
        ["carsai01"] = (32, 19), ["chalec01"] = (32, 19), ["estoco01"] = (32, 19),
        ["feralo01"] = (29, 17), ["fracol01"] = (31, 21), ["gabbor01"] = (31, 20),
        ["georus01"] = (29, 18), ["isahad01"] = (32, 20), ["lannor01"] = (32, 18),
        ["lanstr01"] = (31, 19), ["lewham01"] = (31, 17), ["lialaw01"] = (32, 18),
        ["maxver01"] = (29, 17), ["nichul01"] = (31, 19), ["olibea01"] = (32, 20),
        ["oscpia01"] = (33, 19), ["piegas01"] = (26, 19), ["ryohir01"] = (26, 23),
        ["serper01"] = (30, 19), ["valbot01"] = (32, 18), ["yuktsu01"] = (33, 17)
    };

    private readonly StackPanel _table = new();
    private readonly ScrollViewer _scroller;
    private readonly SolidColorBrush _text = new();
    private readonly SolidColorBrush _heading = new();
    private readonly SolidColorBrush _headerRule = new();
    private readonly SolidColorBrush _rowRule = new();
    private double _minimumTableWidth;

    public NativeResultsTable()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _scroller = new ScrollViewer
        {
            Content = _table,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = _scroller;
        _scroller.SizeChanged += (_, _) => ResizeTable();
        ActualThemeChanged += (_, _) => ApplyPalette();
        ApplyPalette();
    }

    internal void ShowDrivers(IReadOnlyList<StandingRow> rows) => Draw(
        "Driver standings", DriverHeaders, DriverWidths, 720,
        rows.Select(row => new Cell[]
        {
            new(row.Position), new(row.Name, row.DriverBadge, IsDriverPortrait: true), new(row.Nationality),
            new(row.Team, row.TeamBadge, BadgeInset: 2), new(row.Points)
        }));

    internal void ShowTeams(IReadOnlyList<StandingRow> rows) => Draw(
        "Team standings", TeamHeaders, TeamWidths, 380,
        rows.Select(row => new Cell[]
        {
            new(row.Position), new(row.Name, row.TeamBadge, row.Team, row.ImageUri, BadgeInset: 2), new(row.Points)
        }));

    internal void ShowRace(IReadOnlyList<RaceClassificationRow> rows) => Draw(
        "Race classification", RaceHeaders, RaceWidths, 940,
        rows.Select(row => new Cell[]
        {
            new(row.Position), new(row.Number), new(row.Driver, row.DriverBadge, IsDriverPortrait: true),
            new(row.Team, row.TeamBadge, BadgeInset: 2),
            new(row.Laps), new(row.Time), new(row.Points)
        }));

    internal void ShowSchedule(IReadOnlyList<RaceSession> sessions)
    {
        var culture = CultureInfo.GetCultureInfo(UiLanguage.Choose("zh-CN", "en-US"));
        var dateFormat = UiLanguage.Choose("M月d日 ddd", "ddd, MMM d");
        Draw(UiLanguage.Choose("分站赛程", "Race weekend schedule"),
            [UiLanguage.Choose("日期", "DATE"), UiLanguage.Choose("比赛环节", "SESSION"), UiLanguage.Choose("系统本地时间", "LOCAL TIME")],
            [27, 31, 42], 360, sessions.Select(session =>
            {
                var start = session.Start.ToLocalTime();
                var time = start.ToString("HH:mm", culture);
                if (session.End is { } endTime)
                {
                    var end = endTime.ToLocalTime();
                    var endFormat = start.Date == end.Date ? "HH:mm" : dateFormat + " HH:mm";
                    time += " – " + end.ToString(endFormat, culture);
                }

                return new Cell[] { new(start.ToString(dateFormat, culture)), new(UiLanguage.SessionName(session.Name)), new(time) };
            }));
    }

    private void Draw(string name, string[] headers, double[] widths, double minWidth, IEnumerable<Cell[]> rows)
    {
        AutomationProperties.SetName(_scroller, name);
        if (_minimumTableWidth != minWidth)
            _scroller.ChangeView(0, null, null, disableAnimation: true);
        _minimumTableWidth = minWidth;
        _table.Children.Clear();
        _table.Children.Add(BuildRow(headers.Select(header => new Cell(header)).ToArray(), widths, isHeader: true, ruled: true));
        var entries = rows.ToArray();
        for (var i = 0; i < entries.Length; i++)
            _table.Children.Add(BuildRow(entries[i], widths, isHeader: false, ruled: i < entries.Length - 1));
        ResizeTable();
    }

    private Border BuildRow(Cell[] cells, double[] widths, bool isHeader, bool ruled)
    {
        var grid = new Grid();
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) });
            var cell = cells[i];
            var last = i == cells.Length - 1;
            var text = new TextBlock
            {
                Text = cell.Text,
                FontFamily = TableFont,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = isHeader ? 14 : 16,
                Foreground = isHeader ? _heading : _text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = last ? TextAlignment.Right : TextAlignment.Left
            };
            FrameworkElement content = text;
            if (cell.Badge is { } badge)
            {
                var badgeColumn = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
                badgeColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                badgeColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = CreateBadge(badge, cell.BadgeInset, cell.IsDriverPortrait);
                if (!string.IsNullOrWhiteSpace(cell.Principal))
                    AddPrincipalTooltip(icon, cell.Principal, cell.PrincipalImage);
                badgeColumn.Children.Add(icon);
                Grid.SetColumn(text, 1);
                badgeColumn.Children.Add(text);
                content = badgeColumn;
            }

            content.Margin = last ? new Thickness(4, 0, 4, 0) : new Thickness(4, 0, 8, 0);
            Grid.SetColumn(content, i);
            grid.Children.Add(content);
        }

        return new Border
        {
            Child = grid,
            Height = isHeader ? 50 : 56.667,
            BorderBrush = isHeader ? _headerRule : _rowRule,
            BorderThickness = new Thickness(0, 0, 0, ruled ? (isHeader ? 2 : 1) : 0)
        };
    }

    private static Grid CreateBadge(TableBadge badge, double inset, bool isDriverPortrait)
    {
        var container = new Grid { Width = 20, Height = 20 };
        container.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(ParseBadgeColor(badge.Background))
        });
        if (badge.ImageUri is { } uri)
        {
            if (isDriverPortrait)
            {
                container.Children.Add(CreateDriverPortrait(uri));
            }
            else
            {
                var image = new ImageBrush { Stretch = Stretch.UniformToFill };
                CachedImage.SetRemoteUri(image, uri);
                container.Children.Add(new Ellipse { Fill = image, Margin = new Thickness(inset) });
            }
        }
        AutomationProperties.SetAccessibilityView(container, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return container;
    }

    private static Canvas CreateDriverPortrait(Uri uri)
    {
        const int diameter = 20;
        var canvas = new Canvas { Width = diameter, Height = diameter };
        var visual = ElementCompositionPreview.GetElementVisual(canvas);
        var circle = visual.Compositor.CreateEllipseGeometry();
        circle.Center = circle.Radius = new Vector2(diameter / 2f);
        visual.Clip = visual.Compositor.CreateGeometricClip(circle);

        var segments = uri.Segments;
        var driverId = segments.Length >= 2 ? segments[^2].TrimEnd('/') : "";
        var center = uri.AbsolutePath.Contains("/common/f1/2026/", StringComparison.OrdinalIgnoreCase) &&
            DriverFaceCenters.TryGetValue(driverId, out var calibrated) ? calibrated : (X: 32d, Y: 19d);
        var image = new Image { Width = diameter, Height = diameter, Stretch = Stretch.UniformToFill };
        if (uri.AbsolutePath.EndsWith("right.webp", StringComparison.OrdinalIgnoreCase))
        {
            const double scale = diameter / 48d;
            // The side portraits have a fixed width. Let WinUI preserve their varying heights.
            image.Stretch = Stretch.Uniform;
            image.Width = 64 * scale;
            image.Height = double.NaN;
            Canvas.SetLeft(image, diameter / 2d - center.X * scale);
            Canvas.SetTop(image, diameter / 2d - center.Y * scale);
        }
        canvas.Children.Add(image);
        CachedImage.SetRemoteUri(image, uri);
        return canvas;
    }

    private static Color ParseBadgeColor(string? hex)
    {
        if (hex is { Length: 7 } && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return Color.FromArgb(255, 128, 128, 128);
    }

    private static void AddPrincipalTooltip(FrameworkElement badge, string name, Uri? portrait)
    {
        var content = new StackPanel { Spacing = 6 };
        var tooltip = new ToolTip { Content = content };
        if (portrait is not null)
        {
            var image = new Image { Width = 160, Height = 90, Stretch = Stretch.UniformToFill };
            content.Children.Add(image);
            tooltip.Opened += (_, _) => CachedImage.SetRemoteUri(image, portrait);
        }
        content.Children.Add(new TextBlock { Text = name });
        ToolTipService.SetToolTip(badge, tooltip);
    }

    private void ResizeTable() => _table.Width = Math.Max(_minimumTableWidth, _scroller.ActualWidth);

    private void ApplyPalette()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        _text.Color = dark ? Colors.White : Colors.Black;
        _heading.Color = dark ? Color.FromArgb(255, 205, 205, 205) : Color.FromArgb(255, 96, 96, 102);
        _headerRule.Color = dark ? Color.FromArgb(112, 255, 255, 255) : Color.FromArgb(85, 0, 0, 0);
        _rowRule.Color = dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(34, 0, 0, 0);
    }
}
