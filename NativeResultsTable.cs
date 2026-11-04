using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ApexF1;

public sealed class NativeResultsTable : UserControl
{
    private sealed record Cell(string Text, TableBadge? Badge = null, string? Principal = null,
        Uri? PrincipalImage = null, double BadgeInset = 0);

    private static readonly string[] DriverHeaders = ["POS.", "DRIVER", "NATIONALITY", "TEAM", "PTS."];
    private static readonly double[] DriverWidths = [12.25, 33.38, 20.34, 26.03, 8];
    private static readonly string[] TeamHeaders = ["POS.", "TEAM", "PTS."];
    private static readonly double[] TeamWidths = [26.5, 63.5, 10];
    private static readonly string[] RaceHeaders = ["POS.", "NO.", "DRIVER", "TEAM", "LAPS", "TIME / RETIRED", "PTS."];
    private static readonly double[] RaceWidths = [9.72, 8.99, 26.4, 22.82, 9.97, 16.1, 6];
    private static readonly FontFamily TableFont = new("ms-appx:///Assets/TitilliumWeb-SemiBold.ttf#Titillium Web SemiBold");

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
            new(row.Position), new(row.Name, row.DriverBadge), new(row.Nationality),
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
            new(row.Position), new(row.Number), new(row.Driver, row.DriverBadge), new(row.Team, row.TeamBadge, BadgeInset: 2),
            new(row.Laps), new(row.Time), new(row.Points)
        }));

    private void Draw(string name, string[] headers, double[] widths, double minWidth, IEnumerable<Cell[]> rows)
    {
        AutomationProperties.SetName(_scroller, name);
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
                var icon = CreateBadge(badge, cell.BadgeInset);
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
            BorderThickness = new Thickness(0, 0, 0, ruled ? (isHeader ? 2 : 0.667) : 0)
        };
    }

    private static Grid CreateBadge(TableBadge badge, double inset)
    {
        var container = new Grid { Width = 20, Height = 20 };
        container.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(ParseBadgeColor(badge.Background))
        });
        if (badge.ImageUri is { } uri)
        {
            var image = new ImageBrush { Stretch = Stretch.UniformToFill };
            CachedImage.SetRemoteUri(image, uri);
            container.Children.Add(new Ellipse { Fill = image, Margin = new Thickness(inset) });
        }
        AutomationProperties.SetAccessibilityView(container, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return container;
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
        _table.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 28, 28, 28) : Colors.White);
        _text.Color = dark ? Colors.White : Colors.Black;
        _heading.Color = dark ? Color.FromArgb(255, 205, 205, 205) : Color.FromArgb(255, 96, 96, 102);
        _headerRule.Color = dark ? Color.FromArgb(255, 116, 116, 116) : Color.FromArgb(255, 170, 170, 170);
        _rowRule.Color = dark ? Color.FromArgb(255, 61, 61, 61) : Color.FromArgb(255, 224, 222, 220);
    }
}
