using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public class RegValueVM : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name      { get; set; } = "";
    public string ValueType { get; set; } = "";
    public string Data      { get; set; } = "";
}

public partial class RegistryEditorWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;
    private readonly ObservableCollection<string>    _subKeys = [];
    private readonly ObservableCollection<RegValueVM> _values  = [];
    private string _currentPath = @"HKEY_LOCAL_MACHINE\SOFTWARE";

    public RegistryEditorWindow(TlsServer server, string clientId, string label)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = label;
        TxtPath.Text = _currentPath;
        ListSubKeys.ItemsSource = _subKeys;
        GridValues.ItemsSource  = _values;
        _server.RegisterHandler(clientId, PacketType.RegChildrenResult, OnChildren);
        _server.RegisterHandler(clientId, PacketType.RegAck, OnAck);
        Closed += (_, _) => { _server.UnregisterHandler(clientId, PacketType.RegChildrenResult); _server.UnregisterHandler(clientId, PacketType.RegAck); };
        Navigate(_currentPath);
    }

    private void Navigate(string path)
    {
        _currentPath = path;
        TxtPath.Text = path;
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegGetChildren, Data = JsonConvert.SerializeObject(new RegGetChildrenData { KeyPath = path }) });
    }

    private void OnChildren(Packet pkt)
    {
        var d = JsonConvert.DeserializeObject<RegChildrenResultData>(pkt.Data);
        if (d == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            _subKeys.Clear();
            foreach (var k in d.SubKeys) _subKeys.Add(k);
            _values.Clear();
            foreach (var v in d.Values) _values.Add(new RegValueVM { Name = v.Name, ValueType = v.ValueType, Data = v.Data });
            TxtStatus.Text = string.IsNullOrEmpty(d.Error) ? $"{d.SubKeys.Count} keys, {d.Values.Count} values — {d.KeyPath}" : $"Error: {d.Error}";
        });
    }

    private void OnAck(Packet pkt)
    {
        var d = JsonConvert.DeserializeObject<RegAckData>(pkt.Data);
        if (d == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            TxtStatus.Text = d.Success ? "Done." : $"Error: {d.Error}";
            if (d.Success) Navigate(_currentPath);
        });
    }

    private void SubKey_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (ListSubKeys.SelectedItem is string sub)
            Navigate(_currentPath.TrimEnd('\\') + "\\" + sub);
    }

    private void BtnGo_Click(object s, RoutedEventArgs e) => Navigate(TxtPath.Text.Trim());
    private void TxtPath_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(TxtPath.Text.Trim()); }

    private void BtnNewKey_Click(object s, RoutedEventArgs e)
    {
        var name = SimpleInput("New key name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegCreateKey, Data = JsonConvert.SerializeObject(new RegCreateKeyData { KeyPath = _currentPath.TrimEnd('\\') + "\\" + name }) });
    }

    private void BtnNewValue_Click(object s, RoutedEventArgs e)
    {
        var name = SimpleInput("Value name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var data = SimpleInput("Value data:", "");
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegSetValue, Data = JsonConvert.SerializeObject(new RegSetValueData { KeyPath = _currentPath, Name = name, ValueType = "REG_SZ", Data = data ?? "" }) });
    }

    private void BtnDeleteKey_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Delete key:\n{_currentPath}?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegDeleteKey, Data = JsonConvert.SerializeObject(new RegDeleteKeyData { KeyPath = _currentPath }) });
    }

    private void BtnDeleteValue_Click(object s, RoutedEventArgs e)
    {
        if (GridValues.SelectedItem is not RegValueVM vm) return;
        if (MessageBox.Show($"Delete value \"{vm.Name}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegDeleteValue, Data = JsonConvert.SerializeObject(new RegDeleteValueData { KeyPath = _currentPath, Name = vm.Name }) });
    }

    private void GridValues_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (GridValues.SelectedItem is not RegValueVM vm) return;
        var newData = SimpleInput($"Edit \"{vm.Name}\":", vm.Data);
        if (newData == null || newData == vm.Data) return;
        _ = _server.SendToClient(_clientId, new Packet { Type = PacketType.RegSetValue, Data = JsonConvert.SerializeObject(new RegSetValueData { KeyPath = _currentPath, Name = vm.Name, ValueType = vm.ValueType, Data = newData }) });
    }

    private static string? SimpleInput(string prompt, string? defaultValue = null)
    {
        var dlg = new Window
        {
            Title = prompt, Width = 400, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x22))
        };
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
        var txt = new System.Windows.Controls.TextBox { Text = defaultValue ?? "", Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x2E)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x85, 0xF5)), BorderThickness = new Thickness(1), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 8) };
        var btn = new System.Windows.Controls.Button { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x85, 0xF5)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4) };
        btn.Click += (_, _) => dlg.DialogResult = true;
        txt.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) dlg.DialogResult = true; };
        stack.Children.Add(txt);
        stack.Children.Add(btn);
        dlg.Content = stack;
        txt.SelectAll();
        txt.Focus();
        return dlg.ShowDialog() == true ? txt.Text : null;
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void ResizeGrip_DragDelta(object s, DragDeltaEventArgs e)
    { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
