using LibVLCSharp.Shared;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VideoPlayerApp
{
    public partial class MainWindow : Window
    {
        private LibVLC _libVLC;
        private MediaPlayer _player;

        private DispatcherTimer progressTimer;
        private DispatcherTimer hideControlsTimer;

        private bool isPlaying = false;

        // Current aspect selection (null = fit)
        private string currentAspect = null;

        // ===================== HISTORIAL =====================
        private const int MaxHistory = 5;
        private readonly string historyFile = "history.json";
        private List<HistoryItem> history = new();

        // ===================== CONSTRUCTOR =====================
        public MainWindow()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);

            InitializeComponent();

            videoView.MediaPlayer = _player;

            // Progreso
            progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            progressTimer.Tick += ProgressTimer_Tick;

            // Ocultar controles
            hideControlsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            hideControlsTimer.Tick += (s, e) => HideControls();

            Loaded += (s, e) =>
            {
                LoadHistory();
                hideControlsTimer.Start();
                Keyboard.Focus(this);

                // Recalculate aspect when window resizes
                this.SizeChanged += Window_SizeChanged;
            };
        }

        // ===================== MENÚ =====================
        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("DJ ISLA PLAYER\nVersión 1.0\nDesarrollado por DJStudios Cuba.", "Acerca de", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPosition();
            Close();
        }

        // New aspect ratio handlers
        private void Aspect_16_9_Click(object sender, RoutedEventArgs e)
        {
            SetAspectRatio("16:9");
        }

        private void Aspect_4_3_Click(object sender, RoutedEventArgs e)
        {
            SetAspectRatio("4:3");
        }

        private void Aspect_Fit_Click(object sender, RoutedEventArgs e)
        {
            // Reset to automatic sizing so the control fits the available area
            SetAspectRatio(null);
        }

        private void SetAspectRatio(string ratio)
        {
            // remember the selected aspect so we can recompute on resize
            currentAspect = ratio;

            // If ratio is null or empty -> fit (auto)
            if (string.IsNullOrEmpty(ratio))
            {
                // Clear any fixed size so WPF layout will size the VideoView normally
                videoView.Width = double.NaN;
                videoView.Height = double.NaN;
                videoView.HorizontalAlignment = HorizontalAlignment.Stretch;
                videoView.VerticalAlignment = VerticalAlignment.Stretch;
                return;
            }

            // Apply manual aspect by computing desired width/height inside parent
            if (ratio == "16:9")
                ApplyManualAspect(16, 9);
            else if (ratio == "4:3")
                ApplyManualAspect(4, 3);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Defer to dispatcher to ensure layout updated
            Dispatcher.BeginInvoke(new Action(() => RecalculateAspect()));
        }

        private void RecalculateAspect()
        {
            if (string.IsNullOrEmpty(currentAspect))
            {
                // ensure fit mode keeps automatic sizing
                videoView.Width = double.NaN;
                videoView.Height = double.NaN;
                videoView.HorizontalAlignment = HorizontalAlignment.Stretch;
                videoView.VerticalAlignment = VerticalAlignment.Stretch;
                return;
            }

            if (currentAspect == "16:9")
                ApplyManualAspect(16, 9);
            else if (currentAspect == "4:3")
                ApplyManualAspect(4, 3);
        }

        private void ApplyManualAspect(int w, int h)
        {
            if (videoView == null) return;

            var parent = videoView.Parent as FrameworkElement;
            if (parent == null) return;

            // Consider the margins applied to the VideoView
            var margin = videoView.Margin;
            double availableWidth = Math.Max(0, parent.ActualWidth - margin.Left - margin.Right);
            double availableHeight = Math.Max(0, parent.ActualHeight - margin.Top - margin.Bottom);

            // If available sizes are not yet calculated, try to use window size as fallback
            if (availableWidth <= 0 || availableHeight <= 0)
            {
                availableWidth = Math.Max(0, this.ActualWidth - margin.Left - margin.Right);
                availableHeight = Math.Max(0, this.ActualHeight - margin.Top - margin.Bottom - 100); // reserve for controls
            }

            double targetRatio = (double)w / h;
            double parentRatio = availableWidth / Math.Max(1, availableHeight);

            double newWidth, newHeight;

            if (parentRatio > targetRatio)
            {
                // parent is wider -> height limited
                newHeight = availableHeight;
                newWidth = newHeight * targetRatio;
            }
            else
            {
                // parent is taller -> width limited
                newWidth = availableWidth;
                newHeight = newWidth / targetRatio;
            }

            // Set explicit size so the video area keeps the requested aspect
            videoView.Width = Math.Max(0, newWidth);
            videoView.Height = Math.Max(0, newHeight);
            videoView.HorizontalAlignment = HorizontalAlignment.Center;
            videoView.VerticalAlignment = VerticalAlignment.Center;
        }

        // ===================== ARCHIVO =====================
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            ShowControls();

            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Videos|*.mp4;*.mkv;*.avi;*.mov"
            };

            if (dialog.ShowDialog() == true)
            {
                PlayFile(dialog.FileName, 0);
            }
        }

        // ===================== PLAYBACK =====================
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

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            ShowControls();
            _player?.Play();
            isPlaying = true;
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            ShowControls();
            _player?.Pause();
            isPlaying = false;
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            ShowControls();
            _player?.Stop();
            progressTimer.Stop();
            progressBar.Value = 0;
            timeText.Text = "00:00 / 00:00";
            isPlaying = false;
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_player?.Length > 0)
                _player.Time += 10000;
        }

        private void Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_player?.Length > 0)
                _player.Time = Math.Max(0, _player.Time - 10000);
        }

        // ===================== PROGRESO =====================
        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            if (_player == null || _player.Length <= 0) return;

            progressBar.Maximum = _player.Length / 1000.0;
            progressBar.Value = _player.Time / 1000.0;

            timeText.Text =
                $"{TimeSpan.FromMilliseconds(_player.Time):mm\\:ss} / " +
                $"{TimeSpan.FromMilliseconds(_player.Length):mm\\:ss}";
        }

        private void ProgressBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_player != null)
                _player.Time = (long)(progressBar.Value * 1000);
        }

        // ===================== VOLUMEN =====================
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ShowControls();

            if (_player != null)
                _player.Volume = (int)(e.NewValue * 100);
        }

        // ===================== TECLADO =====================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ShowControls();

            if (e.Key == Key.Space)
            {
                if (isPlaying) Pause_Click(null, null);
                else Play_Click(null, null);
            }
            else if (e.Key == Key.Right)
                Forward_Click(null, null);
            else if (e.Key == Key.Left)
                Rewind_Click(null, null);
            else if (e.Key == Key.F11)
                ToggleFullScreen(null, null);
        }

        // ===================== FULLSCREEN =====================
        private void ToggleFullScreen(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
        }

        // ===================== CONTROLES =====================
        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            ShowControls();
        }

        private void ShowControls()
        {
            ControlsPanel?.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));

            Mouse.OverrideCursor = null;

            hideControlsTimer?.Stop();
            hideControlsTimer?.Start();
        }

        private void HideControls()
        {
            if (WindowState != WindowState.Maximized) return;

            ControlsPanel?.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));

            Mouse.OverrideCursor = Cursors.None;
        }

        // ===================== DRAG & DROP =====================
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // Permite arrastrar archivos al área de la ventana
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            // Permite que el efecto de arrastre se quite al salir del área de la ventana
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                    PlayFile(files[0], 0);
            }
        }

        // ===================== HISTORIAL =====================
        private void AddToHistory(string path, long time)
        {
            history.RemoveAll(h => h.Path == path);

            history.Insert(0, new HistoryItem
            {
                Path = path,
                Time = time
            });

            if (history.Count > MaxHistory)
                history.RemoveAt(history.Count - 1);

            SaveHistory();
            RefreshHistoryMenu();
        }

        private void SaveCurrentPosition()
        {
            if (_player?.Media == null) return;

            AddToHistory(_player.Media.Mrl, _player.Time);
        }

        private void RefreshHistoryMenu()
        {
            HistoryMenu.Items.Clear();

            foreach (var item in history)
            {
                var mi = new MenuItem
                {
                    Header = $"{System.IO.Path.GetFileName(item.Path)}"
                };

                mi.Click += (s, e) =>
                {
                    PlayFile(item.Path, item.Time);
                };

                HistoryMenu.Items.Add(mi);
            }
        }

        private void SaveHistory()
        {
            File.WriteAllText(historyFile, JsonSerializer.Serialize(history));
        }

        private void LoadHistory()
        {
            if (!File.Exists(historyFile)) return;

            history = JsonSerializer.Deserialize<List<HistoryItem>>(
                File.ReadAllText(historyFile)) ?? new();

            RefreshHistoryMenu();
        }
    }

    // ===================== MODELO HISTORIAL =====================
    public class HistoryItem
    {
        public string Path { get; set; }
        public long Time { get; set; }
    }
}
