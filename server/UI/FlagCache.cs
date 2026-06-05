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
                Application.Current?.Dispatcher.BeginInvoke(() => client.FlagImage = hit);
            return;
        }

        _ = Task.Run(async () =>
        {
            var img = await DownloadAsync(key);
            _mem[key] = img;
            if (img != null)
                Application.Current?.Dispatcher.BeginInvoke(() => client.FlagImage = img);
        });
    }

    private static async Task<BitmapImage?> DownloadAsync(string key)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, $"{key}.png");
            if (!File.Exists(file))
            {
                var bytes = await _http.GetByteArrayAsync($"https://flagcdn.com/20x15/{key}.png");
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
