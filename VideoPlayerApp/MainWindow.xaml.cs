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
        private string currentAspect = null;

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

            // Timer para la barra de progreso
            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            progressTimer.Tick += ProgressTimer_Tick;

            // Timer para ocultar controles
            hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideControlsTimer.Tick += (s, e) => HideControls();

            Loaded += (s, e) => {
                LoadHistory();
                hideControlsTimer.Start();
                InitializeBrowser();
            };
        }

        private async void InitializeBrowser()
        {
            try { await webView.EnsureCoreWebView2Async(null); }
            catch (Exception) { MessageBox.Show("WebView2 Runtime no encontrado. Instálelo para ver videos online."); }
        }

        // ===================== NAVEGACIÓN ONLINE =====================
        private void Online_Click(object sender, RoutedEventArgs e)
        {
            _player?.Pause();
            isPlaying = false;
            BrowserContainer.Visibility = Visibility.Visible;
            videoView.Visibility = Visibility.Collapsed;
        }

        private void CloseBrowser_Click(object sender, RoutedEventArgs e)
        {
            BrowserContainer.Visibility = Visibility.Collapsed;
            videoView.Visibility = Visibility.Visible;
        }

        private void Legal_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("AVISO LEGAL - DJSTUDIOS CUBA\n\n" +
                "El contenido visualizado en el navegador interno pertenece a terceros.\n" +
                "DJStudios Cuba no se hace responsable por el contenido, la seguridad o el uso que el usuario haga de sitios externos.\n" +
                "Cualquier responsabilidad recae exclusivamente en el usuario final.",
                "Descargo de Responsabilidad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ===================== REPRODUCCIÓN =====================
        private void PlayFile(string path, long startTime)
        {
            if (!File.Exists(path)) return;
            var media = new Media(_libVLC, new Uri(path));
            _player.Play(media);
            _player.Time = startTime;
            progressTimer.Start();
            isPlaying = true;
            AddToHistory(path, startTime);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov" };
            if (dialog.ShowDialog() == true) PlayFile(dialog.FileName, 0);
        }

        private void Play_Click(object sender, RoutedEventArgs e) { _player.Play(); isPlaying = true; ShowControls(); }
        private void Pause_Click(object sender, RoutedEventArgs e) { _player.Pause(); isPlaying = false; ShowControls(); }
        private void Stop_Click(object sender, RoutedEventArgs e) { _player.Stop(); isPlaying = false; progressTimer.Stop(); }

        // ===================== ASPECT RATIO =====================
        private void Aspect_16_9_Click(object sender, RoutedEventArgs e) => SetAspectRatio("16:9");
        private void Aspect_4_3_Click(object sender, RoutedEventArgs e) => SetAspectRatio("4:3");
        private void Aspect_Fit_Click(object sender, RoutedEventArgs e) => SetAspectRatio(null);

        private void SetAspectRatio(string ratio)
        {
            currentAspect = ratio;
            if (string.IsNullOrEmpty(ratio)) { videoView.Width = double.NaN; videoView.Height = double.NaN; }
            else RecalculateAspect();
        }

        private void RecalculateAspect()
        {
            if (currentAspect == "16:9") ApplyAspect(16, 9);
            else if (currentAspect == "4:3") ApplyAspect(4, 3);
        }

        private void ApplyAspect(int w, int h)
        {
            double availW = ActualWidth;
            double availH = ActualHeight - 114;
            double target = (double)w / h;
            if ((availW / availH) > target) { videoView.Height = availH; videoView.Width = availH * target; }
            else { videoView.Width = availW; videoView.Height = availW / target; }
        }

        // ===================== UI Y TECLADO =====================
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

        // ===================== HISTORIAL =====================
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
        private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("DJ ISLA PLAYER\nV2.1.0", "DJStudios Cuba");
        private void Forward_Click(object sender, RoutedEventArgs e) => _player.Time += 10000;
        private void Rewind_Click(object sender, RoutedEventArgs e) => _player.Time = Math.Max(0, _player.Time - 10000);
        private void Window_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) PlayFile(((string[])e.Data.GetData(DataFormats.FileDrop))[0], 0); }
    }

    public class HistoryItem { public string Path { get; set; } public long Time { get; set; } }
}