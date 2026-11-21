using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DanceLab.Capture;
using DanceLab.Core.Canonical;
using DanceLab.Core.Reconstruction;

namespace PlayerTrial
{
    /// <summary>
    /// Окно пробного плеера (specs/player/trial.md): слева видео эталонного танцора со звуком (MediaElement,
    /// часы пробы = Position), справа сцена двух призраков с HUD. Управление: Space пауза/плей, R рестарт с нуля.
    /// Код-построенный UI по образцу tools/sensor_view.
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1b, 0x1c, 0x22));
        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xea));

        private readonly Options opts;
        private readonly ComboBox trackerBox = new ComboBox { Width = 150, Margin = new Thickness(10, 8, 8, 8) };
        private readonly Button sensorButton = new Button { Content = "Старт сенсора", Width = 110, Margin = new Thickness(0, 8, 8, 8) };
        private readonly Button recordButton = new Button { Content = "Запись", Width = 90, Margin = new Thickness(0, 8, 8, 8) };
        private LiveRecorder? recorder;
        private readonly TextBlock status = new TextBlock { Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly MediaElement media = new MediaElement { Stretch = Stretch.Uniform, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };
        private readonly GhostCanvas ghost = new GhostCanvas();
        private readonly TextBlock hud = new TextBlock
        {
            Foreground = Text,
            Background = new SolidColorBrush(Color.FromArgb(0xa0, 0x10, 0x11, 0x14)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            Padding = new Thickness(8),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly StopwatchLike fallbackClock = new StopwatchLike();

        private CanonicalTrack? track;
        private TrialScorer? scorer;
        private TrialScore? lastScore;
        private PlayerSession? session;
        private bool busy;
        private bool wantRunning;
        private bool paused;
        private bool mediaOpened;
        private bool videoOk;
        private int tick;
        private double lastScoredT = double.NaN;
        private DateTime? lastUserSeen;
        private GhostLengths ghostLen = GhostLengths.FromLeg(0.8);

        // калибровка: медианы длин костей по первым секундам живого скелета, потом фиксированы
        private const double CalibrationSeconds = 5.0;
        private readonly List<double> calArmLU = new List<double>();
        private readonly List<double> calArmLL = new List<double>();
        private readonly List<double> calArmRU = new List<double>();
        private readonly List<double> calArmRL = new List<double>();
        private readonly List<double> calLegLU = new List<double>();
        private readonly List<double> calLegLL = new List<double>();
        private readonly List<double> calLegRU = new List<double>();
        private readonly List<double> calLegRL = new List<double>();
        private DateTime? calibStart;
        private long lastCalibUs = -1;
        private bool calibrated;
        private IReadOnlyDictionary<string, LimbLengths>? userBones;
        private double userLegM;

        /// <summary>Мини-часы на случай, когда видео не нашлось: призраки всё равно двигаются по реальному времени.</summary>
        private sealed class StopwatchLike
        {
            private readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            public double TotalSeconds => sw.Elapsed.TotalSeconds;

            public void Restart() => sw.Restart();
        }

        public MainWindow(Options options)
        {
            opts = options;
            Title = "DanceLab: пробный плеер (" + opts.Seq + ")";
            Width = 1320;
            Height = 780;
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14));

            trackerBox.Items.Add("Kinect SDK 2.0");
            trackerBox.Items.Add("Nuitrack нативный");
            trackerBox.Items.Add("Nuitrack AI");
            trackerBox.SelectedIndex = IndexOfTracker(opts.Tracker);
            trackerBox.SelectionChanged += async (s, e) => await OnTrackerChanged();
            sensorButton.Click += async (s, e) => await OnSensorButton();
            recordButton.Click += (s, e) => ToggleRecord();

            var bar = new DockPanel { Background = Panel, LastChildFill = false };
            DockPanel.SetDock(status, Dock.Right);
            bar.Children.Add(status);
            bar.Children.Add(trackerBox);
            bar.Children.Add(sensorButton);
            bar.Children.Add(recordButton);

            media.MediaOpened += (s, e) =>
            {
                mediaOpened = true;
                paused = false;
            };
            media.MediaFailed += (s, e) => status.Text = "Видео не открылось: " + e.ErrorException.Message;
            media.MediaEnded += (s, e) => paused = true;

            var videoPanel = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0b, 0x0e)), Margin = new Thickness(10, 0, 5, 10) };
            videoPanel.Children.Add(media);

            var ghostPanel = new Grid { Margin = new Thickness(5, 0, 10, 10) };
            ghostPanel.Children.Add(ghost);
            ghostPanel.Children.Add(hud);

            var views = new Grid();
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(videoPanel, 0);
            Grid.SetColumn(ghostPanel, 1);
            views.Children.Add(videoPanel);
            views.Children.Add(ghostPanel);

            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(views);
            Content = root;

            status.Text = "Загрузка...";
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Space)
                {
                    e.Handled = true;
                    TogglePlay();
                }
                else if (e.Key == Key.R)
                {
                    e.Handled = true;
                    RestartFromZero();
                }
            };
            timer.Tick += (s, e) => OnTick();
            timer.Start();
            Closing += (s, e) => StopNow();
            Loaded += async (s, e) => await ApplyStartup();
        }

        /// <summary>Пуск: эталон, видео, сенсор (--no-sensor пропускает), затем снимок --shot.</summary>
        private async Task ApplyStartup()
        {
            var canonicalPath = Path.Combine(RepoRoot(), "data", "derived", "trial", opts.Seq + ".canonical.json");
            try
            {
                track = CanonicalTrack.Load(canonicalPath);
                scorer = new TrialScorer(track, new TrialParams());
                ghostLen = GhostLengths.FromLeg(track.LegLengthM);
                ghost.SetLengths(ghostLen);
            }
            catch (Exception ex)
            {
                status.Text = "Эталон не загрузился (" + canonicalPath + "): " + ex.Message;
            }

            var videoPath = FindVideo(track?.Video);
            if (videoPath != null)
            {
                videoOk = true;
                media.Source = new Uri(videoPath);
                media.Play();
            }
            else
            {
                videoOk = false;
                fallbackClock.Restart();
                status.Text = "Видео " + (track?.Video ?? "?") + " не найдено в data/aist/video: призраки идут по реальным часам, без звука.";
            }

            if (!opts.NoSensor)
            {
                wantRunning = true;
                await StartAsync();
            }
            else if (videoOk)
            {
                status.Text = "Идёт проба: " + opts.Seq + ", без сенсора (--no-sensor). Space: пауза/плей, R: рестарт.";
            }

            await ShotIfRequested();
        }

        private void OnTick()
        {
            tick++;
            if (track == null)
            {
                return;
            }

            var t = ClockSeconds();
            ghost.SetReference(track.Sample(t));
            UpdateLive(t);
            if (tick % 6 == 0)
            {
                UpdateHud(t);
            }
        }

        /// <summary>Живой пользователь: ближайший к сенсору (минимум |z| корня), калибровка, канонизация, скоринг.</summary>
        private void UpdateLive(double t)
        {
            var s = session;
            if (opts.NoSensor || s == null)
            {
                ghost.SetUser(null, null);
                return;
            }

            var frame = s.Latest;
            if (frame == null || frame.Users.Count == 0)
            {
                ghost.SetUser(null, null);
                return;
            }

            var userId = NearestUserId(frame, s.TrackerId);
            if (userId < 0 || !s.TryGetUser(userId, out var pose, out var lengths))
            {
                ghost.SetUser(null, null);
                return;
            }

            CollectCalibration(lengths, frame.DeviceTimeUs);
            var legM = calibrated && userLegM > 0 ? userLegM : track!.LegLengthM;
            var userFrame = Canonicalizer.ToCanonical(pose, userBones, legM);

            // скоринг по часам видео, раз на новый момент времени (пауза не копит историю)
            if (scorer != null && !paused && !(Math.Abs(t - lastScoredT) < 1e-9))
            {
                lastScore = scorer.Update(t, userFrame);
                lastScoredT = t;
            }

            // краска костей: сравнение на применённом сдвиге скорера
            var shift = lastScore?.ShiftSec ?? 0;
            var comparison = TrialComparer.Compare(track!.Sample(t - shift), userFrame);
            ghost.SetUser(userFrame, comparison.BoneDeviationRad);
            if (comparison.Coverage > 0)
            {
                lastUserSeen = DateTime.UtcNow;
            }
        }

        /// <summary>Пока не откалибровано: копим длины костей реконструктора (раз на кадр); после CalibrationSeconds считаем медианы и фиксируем.</summary>
        private void CollectCalibration(IReadOnlyDictionary<string, LimbLengths> lengths, long deviceTimeUs)
        {
            if (calibrated || deviceTimeUs == lastCalibUs)
            {
                return;
            }

            lastCalibUs = deviceTimeUs;
            calibStart ??= DateTime.UtcNow;
            if (lengths.TryGetValue("ArmLeft", out var al))
            {
                calArmLU.Add(al.Upper);
                calArmLL.Add(al.Lower);
            }

            if (lengths.TryGetValue("ArmRight", out var ar))
            {
                calArmRU.Add(ar.Upper);
                calArmRL.Add(ar.Lower);
            }

            if (lengths.TryGetValue("LegLeft", out var ll))
            {
                calLegLU.Add(ll.Upper);
                calLegLL.Add(ll.Lower);
            }

            if (lengths.TryGetValue("LegRight", out var lr))
            {
                calLegRU.Add(lr.Upper);
                calLegRL.Add(lr.Lower);
            }

            if (calibStart.HasValue && (DateTime.UtcNow - calibStart.Value).TotalSeconds < CalibrationSeconds)
            {
                return;
            }

            var uArm = Median(calArmLU, calArmRU);
            var lArm = Median(calArmLL, calArmRL);
            var thigh = Median(calLegLU, calLegRU);
            var shin = Median(calLegLL, calLegRL);
            if (thigh <= 0 || shin <= 0)
            {
                // вырожденное окно (ноги не выучились): собираем заново, остаёмся на длинах эталона
                calibStart = DateTime.UtcNow;
                lastCalibUs = -1;
                calArmLU.Clear(); calArmLL.Clear(); calArmRU.Clear(); calArmRL.Clear();
                calLegLU.Clear(); calLegLL.Clear(); calLegRU.Clear(); calLegRL.Clear();
                return;
            }

            calibrated = true;

            userLegM = thigh + shin;
            var dict = new Dictionary<string, LimbLengths>();
            if (uArm > 0 && lArm > 0)
            {
                dict["ArmLeft"] = new LimbLengths(uArm, lArm);
                dict["ArmRight"] = new LimbLengths(uArm, lArm);
            }

            dict["LegLeft"] = new LimbLengths(thigh, shin);
            dict["LegRight"] = new LimbLengths(thigh, shin);
            userBones = dict;
            ghostLen = GhostLengths.FromLeg(userLegM);
            ghostLen.UpperArm = uArm > 0 ? uArm : ghostLen.UpperArm;
            ghostLen.LowerArm = lArm > 0 ? lArm : ghostLen.LowerArm;
            ghost.SetLengths(ghostLen);
        }

        /// <summary>Медиана объединения левой и правой конечности.</summary>
        private static double Median(List<double> a, List<double> b)
        {
            var all = new List<double>(a.Count + b.Count);
            all.AddRange(a);
            all.AddRange(b);
            if (all.Count == 0)
            {
                return 0;
            }

            all.Sort((x, y) => x.CompareTo(y));
            return all.Count % 2 == 1 ? all[all.Count / 2] : 0.5 * (all[(all.Count / 2) - 1] + all[all.Count / 2]);
        }

        private void UpdateHud(double t)
        {
            var inv = CultureInfo.InvariantCulture;
            var timeLine = "Время: " + TimeSpan.FromSeconds(t).ToString(@"mm\:ss", inv);
            if (opts.NoSensor || session == null)
            {
                hud.Text = "без сенсора" + Environment.NewLine + timeLine;
                return;
            }

            var seen = lastUserSeen.HasValue && (DateTime.UtcNow - lastUserSeen.Value).TotalSeconds <= 1.0;
            var score = lastScore;
            if (!seen || score == null)
            {
                hud.Text = "не вижу" + Environment.NewLine + timeLine;
                return;
            }

            hud.Text = "Бегущий: " + (score.Running01 * 100).ToString("0", inv) + "%   Итог: " + (score.Total01 * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Грубые: " + score.Errors.ToString(inv) + "   Покрытие: " + (score.Coverage * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Сдвиг: " + score.ShiftSec.ToString("0.00", inv) + " с   " + timeLine;
        }

        private double ClockSeconds()
        {
            return mediaOpened && videoOk ? media.Position.TotalSeconds : fallbackClock.TotalSeconds;
        }

        private void TogglePlay()
        {
            if (!mediaOpened)
            {
                return;
            }

            if (paused)
            {
                media.Play();
                paused = false;
            }
            else
            {
                media.Pause();
                paused = true;
            }
        }

        private void RestartFromZero()
        {
            paused = false;
            lastScore = null;
            lastScoredT = double.NaN;
            if (track != null)
            {
                scorer = new TrialScorer(track, new TrialParams());
            }

            if (mediaOpened)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
            else
            {
                fallbackClock.Restart();
            }
        }

        private static int NearestUserId(SkeletonFrame frame, string trackerId)
        {
            var root = Topology.Root(trackerId);
            var best = -1;
            var bestZ = double.MaxValue;
            foreach (var u in frame.Users)
            {
                foreach (var j in u.Joints)
                {
                    if (j.Name == root && Math.Abs(j.Z) < bestZ)
                    {
                        bestZ = Math.Abs(j.Z);
                        best = u.Id;
                    }
                }
            }

            return best;
        }

        /// <summary>Значения параметра --tracker: kinect, nuitrack-native, nuitrack-ai (nuitrack то же, что nuitrack-ai).</summary>
        private static int IndexOfTracker(string name) => name == "nuitrack-native" ? 1 : name == "nuitrack" || name == "nuitrack-ai" ? 2 : 0;

        private string SelectedKey => trackerBox.SelectedIndex == 2 ? "nuitrack-ai" : trackerBox.SelectedIndex == 1 ? "nuitrack-native" : "kinect";

        private async Task OnTrackerChanged()
        {
            if (busy || !wantRunning)
            {
                return;
            }

            await StopAsync();
            await StartAsync();
        }

        private async Task OnSensorButton()
        {
            if (busy)
            {
                return;
            }

            if (wantRunning)
            {
                wantRunning = false;
                await StopAsync();
            }
            else
            {
                wantRunning = true;
                await StartAsync();
            }
        }

        /// <summary>Запись сессии по кнопке (контракт trial, Integration): data/captures/&lt;время&gt;_&lt;трекер&gt;_trial_&lt;секвенция&gt;.</summary>
        private void ToggleRecord()
        {
            if (recorder == null)
            {
                var s = session;
                if (s == null)
                {
                    status.Text = "Запись невозможна: сенсор не запущен.";
                    return;
                }

                var dir = Path.Combine(RepoRoot(), "data", "captures", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + s.Key + "_trial_" + opts.Seq);
                recorder = LiveRecorder.Start(s.Source, dir, "trial_" + opts.Seq, s.Version, s.Key == "nuitrack-ai" ? "CNN_HPE" : null);
                recordButton.Content = "Стоп записи";
                status.Text = "Запись: " + recorder.Path;
                return;
            }

            var frames = StopRecorder();
            status.Text = "Запись остановлена: " + frames + " кадров.";
        }

        private int StopRecorder()
        {
            var r = recorder;
            recorder = null;
            recordButton.Content = "Запись";
            try
            {
                return r?.Stop() ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private async Task StartAsync()
        {
            busy = true;
            trackerBox.IsEnabled = false;
            sensorButton.IsEnabled = false;
            var key = SelectedKey;
            status.Text = key == "nuitrack-ai" ? "Запускаю Nuitrack AI (загрузка нейросети, несколько секунд)..." : key == "nuitrack-native" ? "Запускаю Nuitrack нативный..." : "Запускаю Kinect SDK...";
            PlayerSession? created = null;
            string? error = null;
            await Task.Run(() =>
            {
                try
                {
                    created = PlayerSession.Create(key);
                    created.Start();
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
                status.Text = "Сенсор не запустился: " + (error ?? "неизвестная ошибка") + " (видео и эталон-призрак продолжают идти)";
                sensorButton.Content = "Старт сенсора";
            }
            else
            {
                session = created;
                sensorButton.Content = "Стоп сенсора";
                status.Text = "Идёт проба: " + opts.Seq + ", трекер " + key + ".";
            }

            busy = false;
            trackerBox.IsEnabled = true;
            sensorButton.IsEnabled = true;
        }

        private async Task StopAsync()
        {
            busy = true;
            trackerBox.IsEnabled = false;
            sensorButton.IsEnabled = false;
            status.Text = "Останавливаю сенсор...";
            StopRecorder();
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
            ghost.SetUser(null, null);
            sensorButton.Content = "Старт сенсора";
            if (!wantRunning)
            {
                status.Text = "Сенсор остановлен.";
            }

            busy = false;
            trackerBox.IsEnabled = true;
            sensorButton.IsEnabled = true;
        }

        private void StopNow()
        {
            timer.Stop();
            StopRecorder();
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

        private string? FindVideo(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            var root = RepoRoot();
            var direct = Path.Combine(root, "data", "aist", "video", "10M", fileName);
            if (File.Exists(direct))
            {
                return direct;
            }

            var dir = Path.Combine(root, "data", "aist", "video");
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
                {
                    return f;
                }
            }

            return null;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DanceLab.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Визуальная приёмка: --shot файл.png [--shot-after сек], после снимка окно закрывается.</summary>
        private async Task ShotIfRequested()
        {
            var shot = opts.ShotPath;
            if (shot == null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(opts.ShotAfterSec));
            var full = Path.IsPathRooted(shot) ? shot : Path.Combine(RepoRoot(), shot);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var content = (FrameworkElement)Content;
            var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(full))
            {
                enc.Save(fs);
            }

            Close();
        }
    }
}
