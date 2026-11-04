using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace ApexF1;

public static class CachedImage
{
    public static readonly DependencyProperty RemoteUriProperty = DependencyProperty.RegisterAttached(
        "RemoteUri", typeof(Uri), typeof(CachedImage), new PropertyMetadata(null, RemoteUriChanged));

    public static Uri? GetRemoteUri(DependencyObject element) => (Uri?)element.GetValue(RemoteUriProperty);

    public static void SetRemoteUri(DependencyObject element, Uri? value) => element.SetValue(RemoteUriProperty, value);

    private static async void RemoteUriChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not (Image or ImageBrush))
            return;

        SetSource(sender, null);
        if (args.NewValue is not Uri uri)
            return;

        var decodeWidth = sender is ImageBrush ? 96 : 640;
        var path = await ImageCache.GetFileAsync(uri);
        if (GetRemoteUri(sender) != uri)
            return;

        if (path is not null)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth };
                await bitmap.SetSourceAsync(stream);
                if (GetRemoteUri(sender) == uri)
                    SetSource(sender, bitmap);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.Runtime.InteropServices.COMException or ArgumentException)
            {
            }
        }

        if (GetRemoteUri(sender) == uri)
            SetSource(sender, new BitmapImage(uri) { DecodePixelWidth = decodeWidth });
    }

    private static void SetSource(DependencyObject sender, ImageSource? source)
    {
        if (sender is Image image)
            image.Source = source;
        else if (sender is ImageBrush brush)
            brush.ImageSource = source;
    }
}
