using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class TcpManagerWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;
    private readonly ObservableCollection<TcpEntryVM> _entries = [];

    public TcpManagerWindow(TlsServer server, string clientId, string clientLabel)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text  = clientLabel;
        GridTcp.ItemsSource = _entries;

        _server.RegisterHandler(clientId, PacketType.TcpListResult, OnTcpList);

        Closed += (_, _) => _server.UnregisterHandler(clientId, PacketType.TcpListResult);
        Loaded += async (_, _) => await Refresh();
    }

    private async Task Refresh()
    {
        TxtStatus.Text = "Refreshing…";
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.TcpGetList });
    }

    private void OnTcpList(Packet pkt)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<TcpListResultData>(pkt.Data);
            if (data == null) return;
            Dispatcher.Invoke(() =>
            {
                _entries.Clear();
                foreach (var e in data.Entries)
                    _entries.Add(new TcpEntryVM(e.Pid, e.ProcessName, e.LocalAddr, e.RemoteAddr, e.State));
                TxtStatus.Text = $"{_entries.Count} connection(s) — {DateTime.Now:HH:mm:ss}";
            });
        }
        catch { }
    }

    private async void Refresh_Click(object s, RoutedEventArgs e) => await Refresh();

    private async void CloseConn_Click(object s, RoutedEventArgs e)
    {
        if (GridTcp.SelectedItem is not TcpEntryVM row) return;
        var data = JsonConvert.SerializeObject(new TcpCloseData { LocalAddr = row.LocalAddr, RemoteAddr = row.RemoteAddr });
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.TcpClose, Data = data });
        await Task.Delay(300);
        await Refresh();
    }

    private void KillProc_Click(object s, RoutedEventArgs e)
    {
        // Send kill via remote shell
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}

public record TcpEntryVM(int Pid, string ProcessName, string LocalAddr, string RemoteAddr, string State);
