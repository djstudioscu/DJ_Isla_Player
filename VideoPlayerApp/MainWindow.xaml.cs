using LibVLCSharp.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DJ_Isla_Player
{
    public partial class MainWindow : Window
    {
        private LibVLC _libVLC;
        private MediaPlayer _player;
        private DispatcherTimer progressTimer;
        private DispatcherTimer hideControlsTimer;
        private bool isPlaying = false;
        private const int MaxHistory = 5;
        private readonly string historyFile = "history.json";
        private List<HistoryItem> history = new();

        public MainWindow()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);
            InitializeComponent();
            videoView.MediaPlayer = _player;

            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            progressTimer.Tick += ProgressTimer_Tick;

            hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideControlsTimer.Tick += (s, e) => HideControls();

            Loaded += (s, e) => { LoadHistory(); InitializeBrowser(); hideControlsTimer.Start(); };
        }

        private async void InitializeBrowser() { try { await webView.EnsureCoreWebView2Async(null); } catch { } }

        private void Social_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = mi.Tag.ToString(), UseShellExecute = true });
            }
        }

        private void Online_Click(object sender, RoutedEventArgs e)
        {
            _player.Pause(); isPlaying = false;
            BrowserContainer.Visibility = Visibility.Visible;
            videoView.Visibility = Visibility.Collapsed;
        }

        private void CloseBrowser_Click(object sender, RoutedEventArgs e)
        {
            BrowserContainer.Visibility = Visibility.Collapsed;
            videoView.Visibility = Visibility.Visible;
        }

        private void Legal_Click(object sender, RoutedEventArgs e) => MessageBox.Show("DJStudios Cuba no se responsabiliza por contenido de terceros.", "Aviso Legal");

        private void PlayFile(string path, long startTime)
        {
            if (!File.Exists(path)) return;
            _player.Play(new Media(_libVLC, new Uri(path)));
            _player.Time = startTime;
            progressTimer.Start(); isPlaying = true;
            AddToHistory(path, startTime);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Video Files|*.mp4;*.mkv;*.avi" };
            if (dialog.ShowDialog() == true) PlayFile(dialog.FileName, 0);
        }

        private void Play_Click(object sender, RoutedEventArgs e) { _player.Play(); isPlaying = true; }
        private void Pause_Click(object sender, RoutedEventArgs e) { _player.Pause(); isPlaying = false; }
        private void Stop_Click(object sender, RoutedEventArgs e) { _player.Stop(); isPlaying = false; }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            if (_player.Length <= 0) return;
            progressBar.Maximum = _player.Length / 1000.0;
            progressBar.Value = _player.Time / 1000.0;
            timeText.Text = $"{TimeSpan.FromMilliseconds(_player.Time):mm\\:ss} / {TimeSpan.FromMilliseconds(_player.Length):mm\\:ss}";
        }

        private void ProgressBar_MouseUp(object sender, MouseButtonEventArgs e) => _player.Time = (long)(progressBar.Value * 1000);
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_player != null) _player.Volume = (int)(e.NewValue * 100); }

        private void ToggleFullScreen(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal) { WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
            else { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = WindowState.Normal; }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { if (isPlaying) Pause_Click(null, null); else Play_Click(null, null); }
            else if (e.Key == Key.F11) ToggleFullScreen(null, null);
        }

        private void Grid_MouseMove(object sender, MouseEventArgs e) => ShowControls();
        private void ShowControls() { ControlsPanel.Opacity = 1; Mouse.OverrideCursor = null; hideControlsTimer.Stop(); hideControlsTimer.Start(); }
        private void HideControls() { if (WindowState == WindowState.Maximized) { ControlsPanel.Opacity = 0; Mouse.OverrideCursor = Cursors.None; } }

        private void AddToHistory(string path, long time)
        {
            history.RemoveAll(h => h.Path == path);
            history.Insert(0, new HistoryItem { Path = path, Time = time });
            if (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
            SaveHistory(); RefreshHistoryMenu();
        }

        private void RefreshHistoryMenu()
        {
            HistoryMenu.Items.Clear();
            foreach (var item in history)
            {
                var mi = new MenuItem { Header = Path.GetFileName(item.Path) };
                mi.Click += (s, e) => PlayFile(item.Path, item.Time);
                HistoryMenu.Items.Add(mi);
            }
        }

        private void SaveHistory() => File.WriteAllText(historyFile, JsonSerializer.Serialize(history));
        private void LoadHistory() { if (File.Exists(historyFile)) { history = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(historyFile)) ?? new(); RefreshHistoryMenu(); } }
        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
        private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("DJ ISLA PLAYER\nDJStudios Cuba", "Acerca de");
        private void Forward_Click(object sender, RoutedEventArgs e) => _player.Time += 10000;
        private void Rewind_Click(object sender, RoutedEventArgs e) => _player.Time = Math.Max(0, _player.Time - 10000);
        private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) PlayFile(((string[])e.Data.GetData(DataFormats.FileDrop))[0], 0); }
    }

    public class HistoryItem { public string Path { get; set; } public long Time { get; set; } }
}