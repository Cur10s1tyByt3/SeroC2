using System.Diagnostics;
using System.Text.Json;

namespace SeroStub;

internal static class ProcessManagerFeature
{
    internal static string GetProcessList()
    {
        var list = new List<ProcEntryStub>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                list.Add(new ProcEntryStub
                {
                    Pid     = p.Id,
                    Name    = p.ProcessName,
                    Memory  = p.WorkingSet64 / 1024,
                    Title   = p.MainWindowTitle,
                    ExePath = GetExePath(p)
                });
            }
            catch { list.Add(new ProcEntryStub { Pid = p.Id, Name = p.ProcessName }); }
            finally { try { p.Dispose(); } catch { } }
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(
            new ProcListResultStub { Processes = list },
            SeroJson.Default.ProcListResultStub);
    }

    internal static bool Kill(int pid)
    {
        try { Process.GetProcessById(pid).Kill(); return true; }
        catch { return false; }
    }

    private static string GetExePath(Process p)
    {
        try { return p.MainModule?.FileName ?? ""; }
        catch { return ""; }
    }
}

internal class ProcEntryStub
{
    public int    Pid     { get; set; }
    public string Name    { get; set; } = "";
    public long   Memory  { get; set; }
    public string Title   { get; set; } = "";
    public string ExePath { get; set; } = "";
}
internal class ProcListResultStub { public List<ProcEntryStub> Processes { get; set; } = []; }
internal class ProcKillDataStub   { public int Pid { get; set; } }
