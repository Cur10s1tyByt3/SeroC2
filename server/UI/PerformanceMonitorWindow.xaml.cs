using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class PerformanceMonitorWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;

    private readonly List<float> _cpuHistory  = [];
    private readonly List<float> _ramHistory  = [];
    private readonly List<long>  _netSHistory = [];
    private readonly List<long>  _netRHistory = [];
    private const int MaxPoints = 60;

    public PerformanceMonitorWindow(TlsServer server, string clientId, string label)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = label;

        _server.RegisterHandler(clientId, PacketType.PerfMonData, OnPerfData);
        Closed += (_, _) =>
        {
            _server.UnregisterHandler(clientId, PacketType.PerfMonData);
            _ = _server.SendToClient(clientId, new Packet { Type = PacketType.PerfMonStop });
        };

        _ = _server.SendToClient(clientId, new Packet
        {
            Type = PacketType.PerfMonStart,
            Data = JsonConvert.SerializeObject(new PerfMonStartData { IntervalMs = 1000 })
        });
        TxtStatus.Text = "Streaming at 1 s interval…";
    }

    private void OnPerfData(Packet pkt)
    {
        var d = JsonConvert.DeserializeObject<PerfMonData>(pkt.Data);
        if (d == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            // CPU
            AddPoint(_cpuHistory, d.CpuUsage);
            TxtCpu.Text = $"{d.CpuUsage:F1}%";
            SetBar(BarCpu, d.CpuUsage / 100f);
            DrawSparkline(SparkCpu, _cpuHistory, 100f,
                Color.FromRgb(0x4A, 0x85, 0xF5), Color.FromRgb(0x1A, 0x30, 0x60));

            // RAM
            float ramPct = d.RamTotal > 0 ? (float)d.RamUsed / d.RamTotal * 100f : 0f;
            AddPoint(_ramHistory, ramPct);
            TxtRam.Text = $"{d.RamUsed:N0} / {d.RamTotal:N0} MB";
            SetBar(BarRam, ramPct / 100f);
            DrawSparkline(SparkRam, _ramHistory, 100f,
                Color.FromRgb(0x7C, 0x5C, 0xE8), Color.FromRgb(0x28, 0x10, 0x60));

            // Network
            AddPointL(_netSHistory, d.NetworkSentKB);
            AddPointL(_netRHistory, d.NetworkRecvKB);
            TxtNetSent.Text = FormatKB(d.NetworkSentKB);
            TxtNetRecv.Text = FormatKB(d.NetworkRecvKB);
            float maxNet = Math.Max(1f, Math.Max(
                _netSHistory.Count > 0 ? _netSHistory.Max() : 1,
                _netRHistory.Count > 0 ? _netRHistory.Max() : 1));
            DrawDualSparkline(SparkNet, _netSHistory, _netRHistory, maxNet);

            TxtStatus.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        });
    }

    private static void AddPoint(List<float> list, float v)
    { list.Add(v); if (list.Count > MaxPoints) list.RemoveAt(0); }

    private static void AddPointL(List<long> list, long v)
    { list.Add(v); if (list.Count > MaxPoints) list.RemoveAt(0); }

    private static void SetBar(System.Windows.Controls.Border bar, float fraction)
    {
        fraction = Math.Max(0f, Math.Min(1f, fraction));
        var parent = (System.Windows.Controls.Border)bar.Parent;
        bar.Width = Math.Max(0, parent.ActualWidth * fraction);
    }

    private static void DrawSparkline(System.Windows.Controls.Canvas canvas, List<float> data, float max, Color lineColor, Color fillColor)
    {
        canvas.Children.Clear();
        if (data.Count < 2) return;

        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 2 || h < 2) return;

        double step = w / (MaxPoints - 1);
        var pts = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            double x = (MaxPoints - data.Count + i) * step;
            double y = h - (data[i] / max * h);
            pts.Add(new Point(x, y));
        }

        // Fill polygon
        var poly = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x40, fillColor.R, fillColor.G, fillColor.B)), StrokeThickness = 0 };
        poly.Points.Add(new Point((MaxPoints - data.Count) * step, h));
        foreach (var p in pts) poly.Points.Add(p);
        poly.Points.Add(new Point(pts.Last().X, h));
        canvas.Children.Add(poly);

        // Line
        var poly2 = new Polyline { Stroke = new SolidColorBrush(lineColor), StrokeThickness = 1.5 };
        foreach (var p in pts) poly2.Points.Add(p);
        canvas.Children.Add(poly2);
    }

    private static void DrawDualSparkline(System.Windows.Controls.Canvas canvas, List<long> sent, List<long> recv, float max)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 2 || h < 2 || sent.Count < 2) return;

        double step = w / (MaxPoints - 1);

        foreach (var (data, col) in new[] {
            (sent, Color.FromRgb(0x22, 0xC5, 0x5E)),
            (recv, Color.FromRgb(0x4A, 0x85, 0xF5)) })
        {
            if (data.Count < 2) continue;
            var line = new Polyline { Stroke = new SolidColorBrush(col), StrokeThickness = 1.5 };
            for (int i = 0; i < data.Count; i++)
            {
                double x = (MaxPoints - data.Count + i) * step;
                double y = h - (data[i] / max * h);
                line.Points.Add(new Point(x, y));
            }
            canvas.Children.Add(line);
        }
    }

    private static string FormatKB(long kb)
    {
        if (kb >= 1024) return $"{kb / 1024.0:F1} MB/s";
        return $"{kb} KB/s";
    }

    private void Window_MouseLeftButtonDown(object s, System.Windows.Input.MouseButtonEventArgs e)
    { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && WindowState != WindowState.Maximized) DragMove(); }

    private void ResizeGrip_DragDelta(object s, DragDeltaEventArgs e)
    { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
