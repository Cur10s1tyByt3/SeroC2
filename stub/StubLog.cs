namespace SeroStub;

internal static class StubLog
{
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Info(string msg) { }

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Error(string msg) { }

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Debug(string msg) { }
}
