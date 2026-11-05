using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ApexF1;

public static class RaceFlag
{
    private const int FlagRasterWidth = 128;
    private static readonly Uri ChinaFlagAsset = new("ms-appx:///Assets/ChinaFlag.svg");

    public static readonly DependencyProperty SvgProperty = DependencyProperty.RegisterAttached(
        "Svg", typeof(string), typeof(RaceFlag), new PropertyMetadata(null, SvgChanged));

    public static string? GetSvg(DependencyObject element) => (string?)element.GetValue(SvgProperty);

    public static void SetSvg(DependencyObject element, string? value) => element.SetValue(SvgProperty, value);

    private static async void SvgChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Image image)
            return;

        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        if (args.NewValue is not string svg)
            return;

        var source = new SvgImageSource { RasterizePixelWidth = FlagRasterWidth };
        if (svg.Contains("<title>Flag of People’s Republic of China</title>", StringComparison.Ordinal) ||
            svg.Contains("<title>Flag of People's Republic of China</title>", StringComparison.Ordinal) ||
            svg.Contains("<title>Flag of China</title>", StringComparison.Ordinal))
        {
            source.UriSource = ChinaFlagAsset;
            image.Source = source;
            image.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteString(svg);
            await writer.StoreAsync();
            writer.DetachStream();
            stream.Seek(0);

            if (await source.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success && GetSvg(image) == svg)
            {
                image.Source = source;
                image.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // The station name remains visible if Windows cannot decode a flag.
        }
    }
}
