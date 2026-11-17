using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DanceLab.Capture;
using DanceLab.Core.Reconstruction;

namespace SensorView
{
    internal sealed class MainWindow : Window
    {
        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1b, 0x1c, 0x22));
        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xea));

        private readonly ComboBox trackerBox = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button startButton = new Button { Content = "Старт", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        private readonly CheckBox depthBox = new CheckBox { Content = "Глубина", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        private readonly CheckBox reconBox = new CheckBox { Content = "Достройка", IsChecked = true, Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        private readonly CheckBox ghostBox = new CheckBox { Content = "Призрак", IsChecked = true, Foreground = Text, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox limitBox = new TextBox { Text = "3", Width = 36, Margin = new Thickness(6, 0, 12, 0) };
        private readonly TextBox recSecondsBox = new TextBox { Text = "30", Width = 40 };
        private readonly TextBox recNameBox = new TextBox { Text = "viewer", Width = 130 };
        private readonly Button recButton = new Button { Content = "Старт записи", Width = 140 };
        private readonly TextBlock status = new TextBlock { Foreground = Text, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock stats = new TextBlock { Foreground = Text, FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        private readonly Image depthImage = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(10) };
        private readonly SkeletonCanvas front = new SkeletonCanvas(ViewKind.Front, "Вид спереди (x, y)");
        private readonly SkeletonCanvas top = new SkeletonCanvas(ViewKind.Top, "Вид сверху (x, z)");
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        private LiveSession? session;
        private bool busy;
        private bool wantRunning;
        private int tick;
        private WriteableBitmap? depthBitmap;
        private ImageFrame? lastDepthDrawn;

        public MainWindow()
        {
            Title = "DanceLab: просмотр сенсора";
            Width = 1320;
            Height = 780;
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14));

            trackerBox.Items.Add("Kinect SDK 2.0");
            trackerBox.Items.Add("Nuitrack нативный");
            trackerBox.Items.Add("Nuitrack AI");
            trackerBox.SelectedIndex = 0;
            trackerBox.SelectionChanged += async (s, e) => await OnTrackerChanged();
            startButton.Click += async (s, e) => await OnStartStop();
            depthBox.Checked += (s, e) => ApplyDepth();
            depthBox.Unchecked += (s, e) => ApplyDepth();

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0), Background = Panel };
            bar.Margin = new Thickness(0, 0, 0, 10);
            foreach (var el in new FrameworkElement[] { trackerBox, startButton, depthBox, limitBox })
            {
                el.Margin = new Thickness(10, 8, 0, 8);
            }

            status.Margin = new Thickness(12, 0, 0, 0);
            bar.Children.Add(trackerBox);
            bar.Children.Add(startButton);
            bar.Children.Add(depthBox);
            reconBox.Checked += (s, e) => SetRecon(true);
            reconBox.Unchecked += (s, e) => SetRecon(false);
            bar.Children.Add(reconBox);
            ghostBox.Margin = new Thickness(0, 0, 12, 0);
            ghostBox.Checked += (s, e) => SetGhost(true);
            ghostBox.Unchecked += (s, e) => SetGhost(false);
            bar.Children.Add(ghostBox);
            bar.Children.Add(new TextBlock { Text = "Автостоп, мин:", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            bar.Children.Add(limitBox);
            bar.Children.Add(new TextBlock { Text = "Запись, с:", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
            recSecondsBox.Margin = new Thickness(6, 8, 0, 8);
            bar.Children.Add(recSecondsBox);
            bar.Children.Add(new TextBlock { Text = "название:", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            recNameBox.Margin = new Thickness(6, 8, 0, 8);
            bar.Children.Add(recNameBox);
            recButton.Margin = new Thickness(10, 8, 0, 8);
            bar.Children.Add(recButton);
            recButton.Click += (s, e) => OnRecordButton();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && recState != RecState.Idle)
                {
                    e.Handled = true;
                    StopRecordingNow("остановлена по Enter");
                }
            };
            bar.Children.Add(status);

            var right = new DockPanel { Background = Panel };
            DockPanel.SetDock(depthImage, Dock.Bottom);
            depthImage.Height = 260;
            right.Children.Add(depthImage);
            right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stats });

            var views = new Grid();
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            Grid.SetColumn(front, 0);
            Grid.SetColumn(top, 1);
            Grid.SetColumn(right, 2);
            front.Margin = new Thickness(10, 0, 5, 10);
            top.Margin = new Thickness(5, 0, 5, 10);
            right.Margin = new Thickness(5, 0, 10, 10);
            views.Children.Add(front);
            views.Children.Add(top);
            views.Children.Add(right);

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(views);
            Content = root;

            status.Text = "Остановлен. Выберите трекер и нажмите Старт. Сенсор без вентиляции: просмотр остановится сам.";
            timer.Tick += (s, e) => OnTick();
            timer.Start();
            Closing += (s, e) => StopNow();
            Loaded += async (s, e) => await ApplyCommandLine();
        }

        /// <summary>Параметры для проверок без участия человека: --tracker kinect|nuitrack, --depth, --shot файл.png, --shot-after секунды (по умолчанию 8), после снимка окно закрывается.</summary>
        private async Task ApplyCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string? Opt(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            var replayPath = Opt("--replay");
            if (replayPath != null)
            {
                LoadReplay(replayPath, double.TryParse(Opt("--replay-from"), NumberStyles.Float, CultureInfo.InvariantCulture, out var from) ? from : 0);
                await ShotIfRequested(Opt);
                return;
            }

            var tracker = Opt("--tracker");
            if (tracker == null)
            {
                return;
            }

            trackerBox.SelectedIndex = IndexOfTracker(tracker);
            if (Array.IndexOf(args, "--depth") >= 0)
            {
                depthBox.IsChecked = true;
            }

            wantRunning = true;
            var startWatch = System.Diagnostics.Stopwatch.StartNew();
            await StartAsync();
            var created = startWatch.Elapsed.TotalSeconds;
            while (startWatch.Elapsed.TotalSeconds < 60 && session?.Latest == null)
            {
                await Task.Delay(50);
            }

            var startLog = Opt("--start-log");
            if (startLog != null)
            {
                System.IO.File.WriteAllText(startLog, tracker + ": сеанс создан за " + created.ToString("0.0", CultureInfo.InvariantCulture) + " с, первый кадр скелета через " + startWatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с" + Environment.NewLine);
            }

            var autoRecord = Opt("--auto-record");
            if (autoRecord != null)
            {
                recSecondsBox.Text = autoRecord;
                recNameBox.Text = Opt("--record-name") ?? "autotest";
                OnRecordButton();
            }

            var switchTo = Opt("--switch-to");
            if (switchTo != null)
            {
                await Task.Delay(5000);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var target = IndexOfTracker(switchTo);
                var before = session;
                trackerBox.SelectedIndex = target;
                while (busy || session == before || session == null)
                {
                    await Task.Delay(50);
                    if (sw.Elapsed.TotalSeconds > 60)
                    {
                        break;
                    }
                }

                var gotFrame = false;
                while (sw.Elapsed.TotalSeconds < 60 && !gotFrame)
                {
                    gotFrame = session?.Latest != null;
                    await Task.Delay(50);
                }

                System.IO.File.WriteAllText(Opt("--switch-log") ?? "switch.txt", "переключение на " + switchTo + ": сеанс создан за " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с (первый кадр скелета: " + (gotFrame ? "получен" : "нет") + ")" + Environment.NewLine);
            }

            await ShotIfRequested(Opt);
        }

        private async Task ShotIfRequested(Func<string, string?> opt)
        {
            var shot = opt("--shot");
            if (shot == null)
            {
                return;
            }

            var after = double.TryParse(opt("--shot-after"), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 8;
            await Task.Delay(TimeSpan.FromSeconds(after));
            var content = (FrameworkElement)Content;
            var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(shot))
            {
                enc.Save(fs);
            }

            Close();
        }

        private System.Collections.Generic.List<SkeletonFrame>? replay;
        private System.Collections.Generic.List<SkeletonFrame>? replayRecon;
        private string replayTracker = Topology.Kinect;
        private DateTime replayStart;
        private double replayFrom;
        private string replayName = string.Empty;

        private void LoadReplay(string path, double fromSeconds)
        {
            using var reader = new SkeletonRecordReader(System.IO.File.OpenRead(path));
            replayTracker = reader.Header.TrackerId;
            replay = new System.Collections.Generic.List<SkeletonFrame>(reader.ReadFrames());
            replayFrom = fromSeconds;
            replayRecon = new System.Collections.Generic.List<SkeletonFrame>(replay.Count);
            var rec = new MultiUserReconstructor(replayTracker);
            foreach (var rf in replay)
            {
                replayRecon.Add(rec.Process(rf));
            }

            replayStart = DateTime.UtcNow;
            replayName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? path) + " (" + reader.Header.Scenario + ")";
            startButton.IsEnabled = false;
            trackerBox.IsEnabled = false;
            status.Text = "Воспроизведение записи: " + replayName;
        }

        private void OnReplayTick()
        {
            if (replay == null || replay.Count < 2)
            {
                return;
            }

            var t0 = replay[0].DeviceTimeUs;
            var total = (replay[replay.Count - 1].DeviceTimeUs - t0) / 1e6;
            var pos = replayFrom + (DateTime.UtcNow - replayStart).TotalSeconds;
            pos = total > 0 ? pos % total : 0;
            var idx = 0;
            while (idx < replay.Count - 1 && ((replay[idx + 1].DeviceTimeUs - t0) / 1e6) <= pos)
            {
                idx++;
            }

            var f = reconOn && replayRecon != null ? replayRecon[idx] : replay[idx];
            double[]? floor = f.Scene?.FloorPlane;
            if (floor != null)
            {
                lastFloor = floor;
            }

            front.SetData(f, replayTracker, lastFloor, null);
            top.SetData(f, replayTracker, lastFloor, null);
            if (tick++ % 3 == 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("ЗАПИСЬ: " + replayName);
                sb.AppendLine("Трекер: " + replayTracker);
                sb.AppendLine("Время: " + pos.ToString("0.0", CultureInfo.InvariantCulture) + " из " + total.ToString("0.0", CultureInfo.InvariantCulture) + " с (по кругу)");
                sb.AppendLine("Кадр " + idx + " из " + replay.Count);
                sb.AppendLine();
                sb.AppendLine("ЛЮДИ В КАДРЕ: " + f.Users.Count);
                if (f.Users.Count > 0)
                {
                    AppendUser(sb, replayTracker, f.Users[0], lastFloor);
                }

                stats.Text = sb.ToString();
            }
        }

        private enum RecState
        {
            Idle,
            Countdown,
            Recording,
        }

        private const double RecCountdownSeconds = 5;
        private RecState recState = RecState.Idle;
        private DateTime recPhaseStart;
        private double recSeconds = 30;
        private string recScenario = "viewer";

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "DanceLab.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void OnRecordButton()
        {
            if (recState != RecState.Idle)
            {
                StopRecordingNow("остановлена кнопкой");
                return;
            }

            if (session == null || busy)
            {
                status.Text = "Сначала запустите просмотр (Старт), затем запись.";
                return;
            }

            recSeconds = double.TryParse(recSecondsBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 1 ? v : 30;
            var raw = string.IsNullOrWhiteSpace(recNameBox.Text) ? "viewer" : recNameBox.Text.Trim();
            var bad = System.IO.Path.GetInvalidFileNameChars();
            recScenario = new string(Array.ConvertAll(raw.ToCharArray(), c => Array.IndexOf(bad, c) >= 0 || c == ' ' ? '-' : c));
            recState = RecState.Countdown;
            recPhaseStart = DateTime.UtcNow;
            recButton.Content = "Отмена (Enter)";
            recSecondsBox.IsEnabled = false;
            recNameBox.IsEnabled = false;
        }

        private void StopRecordingNow(string reason)
        {
            var was = recState;
            recState = RecState.Idle;
            recButton.Content = "Старт записи";
            recSecondsBox.IsEnabled = true;
            recNameBox.IsEnabled = true;
            front.SetOverlay(null, false);
            top.SetOverlay(null, false);
            if (was == RecState.Countdown)
            {
                status.Text = "Запись отменена до начала.";
                return;
            }

            if (was != RecState.Recording)
            {
                return;
            }

            try
            {
                var r = session?.StopRecording();
                status.Text = r == null ? "Запись остановлена." : "Запись " + reason + ": " + r.Value.Frames + " кадров, папка " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r.Value.Path) ?? r.Value.Path);
            }
            catch (Exception ex)
            {
                status.Text = "Ошибка при сохранении записи: " + ex.Message;
            }
        }

        private void OnRecordTick(LiveSession s)
        {
            if (recState == RecState.Countdown)
            {
                var left = RecCountdownSeconds - (DateTime.UtcNow - recPhaseStart).TotalSeconds;
                if (left > 0)
                {
                    var n = Math.Ceiling(left).ToString("0", CultureInfo.InvariantCulture);
                    front.SetOverlay("Запись через " + n, false);
                    top.SetOverlay("Отойдите от сенсора", false);
                    status.Text = "Запись начнётся через " + n + " с, Enter: отмена.";
                    return;
                }

                try
                {
                    var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    var dir = System.IO.Path.Combine(RepoRoot(), "data", "captures", stamp + "_" + s.Key + "_" + recScenario);
                    s.StartRecording(dir, recScenario);
                    recState = RecState.Recording;
                    recPhaseStart = DateTime.UtcNow;
                    recButton.Content = "Стоп записи (Enter)";
                }
                catch (Exception ex)
                {
                    StopRecordingNow("не началась");
                    status.Text = "Не удалось начать запись: " + ex.Message;
                }

                return;
            }

            if (recState == RecState.Recording)
            {
                var elapsed = (DateTime.UtcNow - recPhaseStart).TotalSeconds;
                var left = recSeconds - elapsed;
                if (left <= 0)
                {
                    StopRecordingNow("завершена");
                    return;
                }

                var text = "● ЗАПИСЬ, осталось " + Math.Ceiling(left).ToString("0", CultureInfo.InvariantCulture) + " с";
                front.SetOverlay(text, true);
                top.SetOverlay("Enter: остановить", true);
                status.Text = text + " (" + (s.Recorder?.FramesWritten ?? 0) + " кадров), Enter: остановить раньше.";
            }
        }

        private bool reconOn = true;

        private void SetRecon(bool on)
        {
            reconOn = on;
            var s = session;
            if (s != null)
            {
                s.UseReconstruction = on;
            }
        }

        private void SetGhost(bool on)
        {
            front.ShowGhost = on;
            top.ShowGhost = on;
            front.InvalidateVisual();
            top.InvalidateVisual();
        }

        private string SelectedKey => trackerBox.SelectedIndex == 2 ? "nuitrack-ai" : trackerBox.SelectedIndex == 1 ? "nuitrack-native" : "kinect";

        /// <summary>Значения параметров --tracker и --switch-to: kinect, nuitrack-native, nuitrack-ai (nuitrack то же, что nuitrack-ai).</summary>
        private static int IndexOfTracker(string name) => name == "nuitrack-native" ? 1 : name == "nuitrack" || name == "nuitrack-ai" ? 2 : 0;

        private async Task OnStartStop()
        {
            if (busy)
            {
                return;
            }

            if (wantRunning)
            {
                wantRunning = false;
                await StopAsync();
                return;
            }

            wantRunning = true;
            await StartAsync();
        }

        private async Task OnTrackerChanged()
        {
            if (busy || !wantRunning)
            {
                return;
            }

            await StopAsync();
            await StartAsync();
        }

        private async Task StartAsync()
        {
            busy = true;
            startButton.IsEnabled = false;
            trackerBox.IsEnabled = false;
            var key = SelectedKey;
            lastFloor = null;
            lastHFov = null;
            status.Text = key == "nuitrack-ai" ? "Запускаю Nuitrack AI (загрузка нейросети, несколько секунд)..." : key == "nuitrack-native" ? "Запускаю Nuitrack нативный..." : "Запускаю Kinect SDK...";
            var withDepth = depthBox.IsChecked == true;
            LiveSession? created = null;
            string? error = null;
            await Task.Run(() =>
            {
                try
                {
                    created = LiveSession.Create(key);
                    created.UseReconstruction = reconOn;
                    created.Start();
                    created.SetDepth(withDepth);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    try
                    {
                        created?.Dispose();
                    }
                    catch (Exception)
                    {
                        // освобождение после неудачного старта, ошибка уже сохранена
                    }

                    created = null;
                }
            });
            if (error != null || created == null)
            {
                wantRunning = false;
                session = null;
                status.Text = "Не удалось запустить: " + (error ?? "неизвестная ошибка");
                startButton.Content = "Старт";
            }
            else
            {
                session = created;
                startButton.Content = "Стоп";
                status.Text = "Идёт просмотр.";
            }

            busy = false;
            startButton.IsEnabled = true;
            trackerBox.IsEnabled = true;
        }

        private async Task StopAsync()
        {
            busy = true;
            startButton.IsEnabled = false;
            trackerBox.IsEnabled = false;
            status.Text = "Останавливаю, освобождаю сенсор...";
            var old = session;
            session = null;
            await Task.Run(() =>
            {
                try
                {
                    old?.Dispose();
                }
                catch (Exception)
                {
                    // сенсор уже освобождён или недоступен
                }
            });
            front.SetData(null, Topology.Kinect, null, null);
            top.SetData(null, Topology.Kinect, null, null);
            startButton.Content = "Старт";
            if (!wantRunning)
            {
                status.Text = "Остановлен.";
            }

            busy = false;
            startButton.IsEnabled = true;
            trackerBox.IsEnabled = true;
        }

        private void StopNow()
        {
            timer.Stop();
            try
            {
                session?.Dispose();
            }
            catch (Exception)
            {
                // закрытие окна
            }

            session = null;
        }

        private void ApplyDepth()
        {
            var s = session;
            if (s == null || busy)
            {
                return;
            }

            var on = depthBox.IsChecked == true;
            Task.Run(() =>
            {
                try
                {
                    s.SetDepth(on);
                }
                catch (Exception)
                {
                    // сеанс уже остановлен
                }
            });
            if (!on)
            {
                depthImage.Source = null;
            }
        }

        private double LimitMinutes()
        {
            return double.TryParse(limitBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 3;
        }

        private async void OnTick()
        {
            if (replay != null)
            {
                OnReplayTick();
                return;
            }

            var s = session;
            if (s == null)
            {
                return;
            }

            tick++;
            if (s.Elapsed.TotalSeconds > 8 && s.Snapshot().Total == 0 && !busy && recState == RecState.Idle)
            {
                status.Text = "Кадров от трекера нет уже " + ((int)s.Elapsed.TotalSeconds) + " с: сенсор мог остаться занят предыдущей программой или процессом. Нажмите Стоп, подождите несколько секунд и запустите снова.";
            }

            OnRecordTick(s);
            var frame = s.Latest;
            double[]? floor = frame?.Scene?.FloorPlane;
            double? hfov = null;
            if (frame?.Scene?.Other != null && frame.Scene.Other.TryGetValue("depthHFovRad", out var h) && h is double hd)
            {
                hfov = hd;
            }

            if (floor != null)
            {
                lastFloor = floor;
            }

            if (hfov.HasValue)
            {
                lastHFov = hfov;
            }

            front.SetData(frame, s.TrackerId, lastFloor, lastHFov);
            top.SetData(frame, s.TrackerId, lastFloor, lastHFov);
            var face = FaceText(frame, out var faceOk);
            front.SetFace(face, faceOk);
            top.SetFace(face, faceOk);

            if (tick % 3 == 0)
            {
                stats.Text = BuildStats(s, frame);
            }

            if (tick % 4 == 0 && depthBox.IsChecked == true)
            {
                DrawDepth(s.LatestDepth);
            }

            if (!busy && recState == RecState.Idle && s.Elapsed.TotalMinutes >= LimitMinutes())
            {
                wantRunning = false;
                await StopAsync();
                status.Text = "Остановлено по таймеру (" + LimitMinutes().ToString("0.#", CultureInfo.InvariantCulture) + " мин).";
            }
        }

        /// <summary>Результат Kinect Face API из Scene.Other кадра: лицо найдено только когда оно обращено к сенсору.</summary>
        private static string? FaceText(SkeletonFrame? frame, out bool ok)
        {
            ok = false;
            if (frame == null || frame.Users.Count == 0)
            {
                return frame?.Scene?.Other != null && frame.Scene.Other.ContainsKey("faceTracked") ? "человека нет" : null;
            }

            var other = frame.Scene?.Other;
            if (other == null || !other.TryGetValue("faceTracked", out var ft) || !(ft is bool tracked))
            {
                return null;
            }

            ok = tracked;
            if (!tracked)
            {
                return "ЛИЦА НЕТ (спиной или боком)";
            }

            var yaw = other.TryGetValue("faceYawDeg", out var y) && y is double yd ? yd : 0;
            return "ЛИЦО НАЙДЕНО, поворот головы " + yaw.ToString("0", CultureInfo.InvariantCulture) + " град";
        }

        private double[]? lastFloor;
        private double? lastHFov;

        private string BuildStats(LiveSession s, SkeletonFrame? frame)
        {
            var inv = CultureInfo.InvariantCulture;
            var snap = s.Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine("Трекер: " + s.TrackerId + " " + s.Version + (s.Engine != null ? " (" + s.Engine + ")" : string.Empty));
            if (s.Sensor.Serial != null)
            {
                sb.AppendLine("Сенсор: " + s.Sensor.Serial);
            }

            if (s.Sensor.Depth != null)
            {
                sb.AppendLine("Глубина: " + s.Sensor.Depth.Width + "x" + s.Sensor.Depth.Height + " " + s.Sensor.Depth.Fps.ToString("0", inv) + " кадр/с (паспорт)");
            }

            sb.AppendLine("Работает: " + s.Elapsed.ToString(@"mm\:ss") + " из " + LimitMinutes().ToString("0.#", inv) + " мин");
            sb.AppendLine();
            sb.AppendLine("СКЕЛЕТ");
            sb.AppendLine("  частота:  " + snap.Fps.ToString("0.0", inv) + " кадр/с");
            sb.AppendLine("  интервал: " + snap.MeanMs.ToString("0.0", inv) + " мс, разброс " + snap.JitterMs.ToString("0.0", inv) + ", максимум " + snap.MaxMs.ToString("0.0", inv));
            sb.AppendLine("  кадров:   " + snap.Total);
            if (depthBox.IsChecked == true)
            {
                sb.AppendLine("ГЛУБИНА");
                sb.AppendLine("  частота:  " + snap.DepthFps.ToString("0.0", inv) + " кадр/с, кадров " + snap.DepthTotal);
            }

            sb.AppendLine();
            if (frame == null)
            {
                sb.AppendLine("Кадров от трекера пока нет.");
                if (s.LastError != null)
                {
                    sb.AppendLine("Ошибка трекера: " + s.LastError);
                }

                return sb.ToString();
            }

            var faceLine = FaceText(frame, out _);
            if (faceLine != null)
            {
                sb.AppendLine(faceLine);
            }

            sb.AppendLine("ЛЮДИ В КАДРЕ: " + frame.Users.Count);
            if (frame.Users.Count > 0)
            {
                AppendUser(sb, s.TrackerId, frame.Users[0], lastFloor);
            }

            sb.AppendLine();
            sb.AppendLine("ПОЛ");
            if (lastFloor == null)
            {
                sb.AppendLine("  трекер плоскость пола не отдаёт");
                sb.AppendLine("  (из глубины: tools/capture_report/scene.ts)");
            }
            else
            {
                var g = FloorGeometry.FromPlane(lastFloor);
                sb.AppendLine("  плоскость: " + string.Join(", ", Array.ConvertAll(lastFloor, v => v.ToString("0.000", inv))));
                if (g.HasValue)
                {
                    sb.AppendLine("  высота сенсора: " + g.Value.HeightM.ToString("0.000", inv) + " м");
                    sb.AppendLine("  наклон: " + g.Value.PitchDeg.ToString("0.0", inv) + " град, крен: " + g.Value.RollDeg.ToString("0.0", inv) + " град");
                }
            }

            if (s.LastError != null)
            {
                sb.AppendLine();
                sb.AppendLine("Ошибка трекера: " + s.LastError);
            }

            return sb.ToString();
        }

        private static void AppendUser(StringBuilder sb, string trackerId, TrackedUser u, double[]? floor)
        {
            var inv = CultureInfo.InvariantCulture;
            int tracked = 0, inferred = 0, missing = 0, rebuilt = 0;
            double confSum = 0;
            int confN = 0;
            RawJoint? ls = null, rs = null, root = null, head = null;
            foreach (var j in u.Joints)
            {
                var gone = j.State == "not-tracked" || j.Conf == 0 || (Math.Abs(j.X) < 0.01 && Math.Abs(j.Y) < 0.01 && Math.Abs(j.Z) < 0.01);
                if (gone)
                {
                    missing++;
                }
                else if (j.State == "reconstructed" || j.State == "extrapolated")
                {
                    rebuilt++;
                }
                else if (j.State == "inferred")
                {
                    inferred++;
                }
                else
                {
                    tracked++;
                }

                if (j.Conf.HasValue && !gone)
                {
                    confSum += j.Conf.Value;
                    confN++;
                }

                if (j.Name == Topology.LeftShoulder(trackerId))
                {
                    ls = j;
                }
                else if (j.Name == Topology.RightShoulder(trackerId))
                {
                    rs = j;
                }
                else if (j.Name == Topology.Root(trackerId))
                {
                    root = j;
                }
                else if (j.Name == "Head")
                {
                    head = j;
                }
            }

            sb.AppendLine("  id " + u.Id + ", суставов: " + tracked + " надёжных, " + inferred + " достроено трекером, " + rebuilt + " достроено нами, " + missing + " нет");
            if (confN > 0)
            {
                sb.AppendLine("  уверенность в среднем: " + (confSum / confN).ToString("0.00", inv));
            }

            if (root != null)
            {
                sb.AppendLine("  расстояние до таза: " + root.Z.ToString("0.00", inv) + " м, смещение x " + root.X.ToString("0.00", inv) + " м");
            }

            if (head != null && floor != null)
            {
                sb.AppendLine("  голова над полом: " + FloorGeometry.HeightAbove(floor, head.X, head.Y, head.Z).ToString("0.00", inv) + " м");
            }

            if (ls != null && rs != null)
            {
                var faces = Topology.FacesSensorByLabels(trackerId, ls.X, rs.X);
                sb.AppendLine("  по меткам плеч: " + (faces ? "лицом к сенсору" : "спиной к сенсору"));
                sb.AppendLine("  ширина плеч: " + Math.Sqrt(((ls.X - rs.X) * (ls.X - rs.X)) + ((ls.Z - rs.Z) * (ls.Z - rs.Z))).ToString("0.00", inv) + " м");
            }
        }

        private void DrawDepth(ImageFrame? f)
        {
            if (f == null || ReferenceEquals(f, lastDepthDrawn) || f.Format != "Depth16" || f.Data.Length < f.Width * f.Height * 2)
            {
                return;
            }

            lastDepthDrawn = f;
            if (depthBitmap == null || depthBitmap.PixelWidth != f.Width || depthBitmap.PixelHeight != f.Height)
            {
                depthBitmap = new WriteableBitmap(f.Width, f.Height, 96, 96, PixelFormats.Gray8, null);
                depthImage.Source = depthBitmap;
            }

            var pixels = new byte[f.Width * f.Height];
            for (var i = 0; i < pixels.Length; i++)
            {
                int mm = f.Data[2 * i] | (f.Data[(2 * i) + 1] << 8);
                if (mm == 0)
                {
                    pixels[i] = 0;
                    continue;
                }

                var t = (mm - 500) / 4000.0;
                t = Math.Max(0, Math.Min(1, t));
                pixels[i] = (byte)(255 - (t * 230));
            }

            depthBitmap.WritePixels(new Int32Rect(0, 0, f.Width, f.Height), pixels, f.Width, 0);
        }
    }
}
