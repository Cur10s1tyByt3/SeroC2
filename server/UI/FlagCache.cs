using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using SeroServer.Data;

namespace SeroServer.UI;

internal static class FlagCache
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> _mem = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SeroServer", "flags");
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Call from TlsServer after country resolution. Fires async; sets client.FlagImage on UI thread.
    internal static void QueueLoad(ConnectedClient client, string code)
    {
        if (string.IsNullOrEmpty(code)) return;
        var key = code.ToLowerInvariant();

        if (_mem.TryGetValue(key, out var hit))
        {
            if (hit != null)
                Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => client.FlagImage = hit);
            return;
        }

        if (key == "lan" || key == "loc")
        {
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                try
                {
                    var text = key == "lan" ? "LAN" : "LOC";
                    var color = key == "lan" ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.SlateGray;
                    var bmp = GenerateBadge(text, color);
                    if (bmp != null)
                    {
                        _mem[key] = bmp;
                        client.FlagImage = bmp;
                    }
                }
                catch { }
            });
            return;
        }

        _ = Task.Run(async () =>
        {
            var img = await DownloadAsync(key);
            _mem[key] = img;
            if (img != null)
                Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => client.FlagImage = img);
        });
    }

    private static BitmapImage? GenerateBadge(string text, System.Windows.Media.Color color)
    {
        try
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(new System.Windows.Media.SolidColorBrush(color), null, new Rect(0, 0, 28, 20), 3, 3);
                var ft = new System.Windows.Media.FormattedText(
                    text,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    10,
                    System.Windows.Media.Brushes.White,
                    1.0);
                dc.DrawText(ft, new Point((28 - ft.Width) / 2, (20 - ft.Height) / 2));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(28, 20, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private static async Task<BitmapImage?> DownloadAsync(string key)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, $"{key}.png");
            if (!File.Exists(file))
            {
                var bytes = await _http.GetByteArrayAsync($"https://flagcdn.com/w40/{key}.png");
                await File.WriteAllBytesAsync(file, bytes);
            }
            return LoadFromFile(file);
        }
        catch { return null; }
    }

    private static BitmapImage? LoadFromFile(string path)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}
