using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace SeroStub;

internal static class ServiceManagerFeature
{
    internal static string GetList()
    {
        var list = new List<ServiceEntryStub>();
        try
        {
            // Use sc.exe to query all services — NativeAOT safe
            var output = RunSc("query type= all state= all");
            ServiceEntryStub? current = null;
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null) list.Add(current);
                    current = new ServiceEntryStub { Name = line[13..].Trim() };
                }
                else if (current != null && line.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
                    current.DisplayName = line[13..].Trim();
                else if (current != null && line.StartsWith("STATE"))
                    current.Status = line.Contains("RUNNING") ? "Running" : line.Contains("STOPPED") ? "Stopped" : "Unknown";
                else if (current != null && line.StartsWith("START_TYPE"))
                    current.StartType = line.Contains("AUTO_START") ? "Auto" : line.Contains("DEMAND_START") ? "Manual" : line.Contains("DISABLED") ? "Disabled" : "Unknown";
            }
            if (current != null) list.Add(current);
        }
        catch { }

        // Enrich with Description + LogOnAs from registry (fast, no extra process spawning)
        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (servicesKey != null)
            {
                foreach (var entry in list)
                {
                    try
                    {
                        using var svcKey = servicesKey.OpenSubKey(entry.Name);
                        if (svcKey == null) continue;
                        var desc = svcKey.GetValue("Description")?.ToString() ?? "";
                        // Expand indirect strings like "@%SystemRoot%\system32\srvsvc.dll,-100"
                        if (!desc.StartsWith('@')) entry.Description = desc;
                        entry.LogOnAs = svcKey.GetValue("ObjectName")?.ToString() ?? "";
                    }
                    catch { }
                }
            }
        }
        catch { }

        list.Sort((a, b) => string.Compare(a.DisplayName.Length > 0 ? a.DisplayName : a.Name,
                                           b.DisplayName.Length > 0 ? b.DisplayName : b.Name,
                                           StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(new SvcListResultStub { Services = list }, SeroJson.Default.SvcListResultStub);
    }

    internal static string DoAction(string action, string serviceName)
    {
        try
        {
            var result = action switch
            {
                "start"   => RunSc($"start \"{serviceName}\""),
                "stop"    => RunSc($"stop \"{serviceName}\""),
                "restart" => RunSc($"stop \"{serviceName}\"") + RunSc($"start \"{serviceName}\""),
                "disable" => RunSc($"config \"{serviceName}\" start= disabled"),
                "delete"  => RunSc($"delete \"{serviceName}\""),
                _          => ""
            };
            var ok = !result.Contains("FAILED") && !result.Contains("error", StringComparison.OrdinalIgnoreCase);
            return JsonSerializer.Serialize(new SvcAckStub { Success = ok, Error = ok ? "" : result.Trim() }, SeroJson.Default.SvcAckStub);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new SvcAckStub { Success = false, Error = ex.Message }, SeroJson.Default.SvcAckStub);
        }
    }

    private static string RunSc(string args)
    {
        try
        {
            // UseShellExecute=false → args not interpreted by cmd.exe, no injection
            using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            return p?.StandardOutput.ReadToEnd() ?? "";
        }
        catch { return ""; }
    }
}
