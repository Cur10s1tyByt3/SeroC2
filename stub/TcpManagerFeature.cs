using System.Runtime.InteropServices;
using System.Text.Json;

namespace SeroStub;

internal static class TcpManagerFeature
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(nint pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int TableClass, int Reserved);

    [DllImport("iphlpapi.dll")]
    private static extern int SetTcpEntry(ref MIB_TCPROW tcpRow);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
    }

    private static readonly string[] TcpStates =
    [
        "UNKNOWN", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD",
        "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT",
        "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"
    ];

    private static uint PortFromDword(uint dw)
        => (uint)(((dw & 0xFF) << 8) | ((dw >> 8) & 0xFF));

    internal static string GetList()
    {
        var entries = new List<TcpEntryStub>();
        try
        {
            int size = 0;
            GetExtendedTcpTable(nint.Zero, ref size, false, 2, 5, 0);
            var buf = Marshal.AllocHGlobal(size + 1024);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, true, 2, 5, 0) == 0)
                {
                    int count = Marshal.ReadInt32(buf);
                    int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                    for (int i = 0; i < count; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buf + 4 + i * rowSize);
                        var localIp = new System.Net.IPAddress(row.dwLocalAddr).ToString();
                        var remoteIp = new System.Net.IPAddress(row.dwRemoteAddr).ToString();
                        var localPort = PortFromDword(row.dwLocalPort);
                        var remotePort = PortFromDword(row.dwRemotePort);
                        var state = row.dwState < (uint)TcpStates.Length ? TcpStates[row.dwState] : $"{row.dwState}";

                        string procName = "";
                        try { using var p = System.Diagnostics.Process.GetProcessById((int)row.dwOwningPid); procName = p.ProcessName; } catch { }

                        entries.Add(new TcpEntryStub
                        {
                            Pid = (int)row.dwOwningPid,
                            ProcessName = procName,
                            LocalAddr = $"{localIp}:{localPort}",
                            RemoteAddr = row.dwState == 2 /*LISTEN*/ ? "*:*" : $"{remoteIp}:{remotePort}",
                            State = state
                        });
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return JsonSerializer.Serialize(new TcpListResultStub { Entries = entries }, SeroJson.Default.TcpListResultStub);
    }

    internal static void Close(string localAddr, string remoteAddr)
    {
        try
        {
            if (!TryParseEndpoint(localAddr, out var localIp, out var localPort)) return;
            if (!TryParseEndpoint(remoteAddr, out var remoteIp, out var remotePort)) return;

            var row = new MIB_TCPROW
            {
                dwState      = 12,  // DELETE_TCB
                dwLocalAddr  = (uint)localIp.Address,
                dwLocalPort  = (uint)((localPort & 0xFF) << 8 | (localPort >> 8)),
                dwRemoteAddr = (uint)remoteIp.Address,
                dwRemotePort = (uint)((remotePort & 0xFF) << 8 | (remotePort >> 8)),
            };
            SetTcpEntry(ref row);
        }
        catch { }
    }

    private static bool TryParseEndpoint(string ep, out System.Net.IPAddress ip, out int port)
    {
        ip = System.Net.IPAddress.Any; port = 0;
        var idx = ep.LastIndexOf(':');
        if (idx < 0) return false;
        if (!System.Net.IPAddress.TryParse(ep[..idx], out ip!)) return false;
        if (!int.TryParse(ep[(idx + 1)..], out port)) return false;
        return true;
    }

    // ── Firewall blocking via netsh advfirewall ──────────────────────────────

    internal static string BlockIp(string remoteIp, string direction)
    {
        try
        {
            var ruleName = $"SeroBlock_IP_{remoteIp.Replace('.', '_').Replace(':', '_')}";
            if (direction is "" or "both" or "in")
                RunNetsh($"advfirewall firewall add rule name=\"{ruleName}_IN\" dir=in action=block remoteip={remoteIp} enable=yes");
            if (direction is "" or "both" or "out")
                RunNetsh($"advfirewall firewall add rule name=\"{ruleName}_OUT\" dir=out action=block remoteip={remoteIp} enable=yes");
            var result = new TcpFirewallRulesResultStub { Rules = [
                new TcpFirewallRuleStub { RuleName = ruleName + "_IN",  ProcessName = "", Port = 0, Direction = "in" },
                new TcpFirewallRuleStub { RuleName = ruleName + "_OUT", ProcessName = "", Port = 0, Direction = "out" }
            ]};
            return System.Text.Json.JsonSerializer.Serialize(result, SeroJson.Default.TcpFirewallRulesResultStub);
        }
        catch { return System.Text.Json.JsonSerializer.Serialize(new TcpFirewallRulesResultStub(), SeroJson.Default.TcpFirewallRulesResultStub); }
    }

    internal static string BlockProcess(string processName, int port, string direction)
    {
        try
        {
            var rules = new List<string>();
            var ruleName = $"SeroBlock_{System.IO.Path.GetFileNameWithoutExtension(processName)}_{port}";

            if (!string.IsNullOrEmpty(processName) && processName != "*")
            {
                var dir = direction == "in" ? "in" : direction == "out" ? "out" : "";
                if (dir == "" || dir == "in")
                    RunNetsh($"advfirewall firewall add rule name=\"{ruleName}_IN\" dir=in action=block program=\"{processName}\" enable=yes");
                if (dir == "" || dir == "out")
                    RunNetsh($"advfirewall firewall add rule name=\"{ruleName}_OUT\" dir=out action=block program=\"{processName}\" enable=yes");
                rules.Add(ruleName + "_IN");
                rules.Add(ruleName + "_OUT");
            }

            if (port > 0)
            {
                var pRuleName = $"SeroBlock_Port{port}";
                var dir = direction == "in" ? "in" : direction == "out" ? "out" : "";
                if (dir == "" || dir == "in")
                    RunNetsh($"advfirewall firewall add rule name=\"{pRuleName}_IN\" dir=in action=block protocol=TCP localport={port} enable=yes");
                if (dir == "" || dir == "out")
                    RunNetsh($"advfirewall firewall add rule name=\"{pRuleName}_OUT\" dir=out action=block protocol=TCP remoteport={port} enable=yes");
                rules.Add(pRuleName + "_IN");
                rules.Add(pRuleName + "_OUT");
            }

            var result = new TcpFirewallRulesResultStub { Rules = rules.Select(r => new TcpFirewallRuleStub { RuleName = r, ProcessName = processName, Port = port, Direction = direction }).ToList() };
            return System.Text.Json.JsonSerializer.Serialize(result, SeroJson.Default.TcpFirewallRulesResultStub);
        }
        catch (Exception ex)
        {
            return System.Text.Json.JsonSerializer.Serialize(new TcpFirewallRulesResultStub(), SeroJson.Default.TcpFirewallRulesResultStub);
        }
    }

    internal static void UnblockRule(string ruleName)
    {
        try { RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\""); }
        catch { }
    }

    internal static string ListFirewallRules()
    {
        var rules = new List<TcpFirewallRuleStub>();
        try
        {
            var output = RunNetshOutput("advfirewall firewall show rule name=all");
            // Parse output to find SeroBlock_ rules
            string? rName = null;
            foreach (var line in output.Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("Rule Name:", StringComparison.OrdinalIgnoreCase))
                {
                    rName = l[(l.IndexOf(':') + 1)..].Trim();
                    if (!rName.StartsWith("SeroBlock_")) rName = null;
                }
                else if (rName != null && l.StartsWith("Direction:", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = l[(l.IndexOf(':') + 1)..].Trim().ToLower();
                    rules.Add(new TcpFirewallRuleStub { RuleName = rName, Direction = dir });
                    rName = null;
                }
            }
        }
        catch { }
        return System.Text.Json.JsonSerializer.Serialize(new TcpFirewallRulesResultStub { Rules = rules }, SeroJson.Default.TcpFirewallRulesResultStub);
    }

    private static void RunNetsh(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netsh", args)
            { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(5000);
    }

    private static string RunNetshOutput(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netsh", args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        return p?.StandardOutput.ReadToEnd() ?? "";
    }
}

internal class TcpEntryStub
{
    public int    Pid         { get; set; }
    public string ProcessName { get; set; } = "";
    public string LocalAddr   { get; set; } = "";
    public string RemoteAddr  { get; set; } = "";
    public string State       { get; set; } = "";
}

internal class TcpListResultStub  { public List<TcpEntryStub> Entries { get; set; } = []; }
internal class TcpCloseDataStub   { public string LocalAddr { get; set; } = ""; public string RemoteAddr { get; set; } = ""; }
