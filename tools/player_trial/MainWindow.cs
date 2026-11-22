using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    /// часы пробы = Position), справа 3D-сцена двух призраков с HUD. Кольцо кнопок камер (S13) меняет ракурс
    /// видео и камеры сцены призраков одной кнопкой (S19). Управление: Space пауза/плей (пауза — режим
    /// подгонки позы, S22: эталон застыл, живая подсветка и мгновенный процент позы), R рестарт с нуля.
    /// Код-построенный UI по образцу tools/sensor_view.
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1b, 0x1c, 0x22));
        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xea));
        private static readonly Brush RingIdle = new SolidColorBrush(Color.FromRgb(0x2a, 0x2c, 0x34));

        /// <summary>Заливка активной кнопки камеры (primary, как LeftColor в SkeletonCanvas).</summary>
        private static readonly Brush RingActive = new SolidColorBrush(Color.FromRgb(0x42, 0xa5, 0xf5));

        private readonly Options opts;
        private readonly ComboBox trackerBox = new ComboBox { Width = 150, Margin = new Thickness(10, 8, 8, 8) };
        private readonly Button sensorButton = new Button { Content = "Старт сенсора", Width = 110, Margin = new Thickness(0, 8, 8, 8) };
        private readonly Button recordButton = new Button { Content = "Запись", Width = 90, Margin = new Thickness(0, 8, 8, 8) };

        // переключатель режимов одного окна (S16): «Проба» / «Сцена сенсора»
        private readonly TextBlock modeLabel = new TextBlock { Foreground = Text, Text = "Режим:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 8, 4, 8) };
        private readonly ComboBox modeBox = new ComboBox { Width = 140, Margin = new Thickness(0, 8, 8, 8) };

        // панель «Сцена сенсора» (S16): Front слева, Top справа, статистика снизу, текст без сессии
        private readonly SceneCanvas sceneFront = new SceneCanvas(false, "вид спереди (x, y)");
        private readonly SceneCanvas sceneTop = new SceneCanvas(true, "вид сверху (x, z)");
        private readonly TextBlock sceneStats = new TextBlock
        {
            Foreground = Text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(8, 4, 8, 8),
        };
        private readonly TextBlock sceneNoSensor = new TextBlock
        {
            Foreground = Text,
            FontSize = 22,
            Text = "сенсор не запущен",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        // вторая строка тулбара: управление воспроизведением (S12), отсчёт попытки (S14), ракурс (S13), скорость (S17)
        private readonly Button playButton = new Button { Content = "⏸ Пауза", Width = 100, Margin = new Thickness(10, 8, 8, 8) };
        private readonly Button restartButton = new Button { Content = "⟳ Рестарт", Width = 100, Margin = new Thickness(0, 8, 8, 8) };
        private readonly Button attemptButton = new Button { Content = "Попытка", Width = 100, Margin = new Thickness(0, 8, 8, 8) };
        private readonly TextBlock countdownLabel = new TextBlock { Foreground = Text, Text = "Отсчёт:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 8, 4, 8) };
        private readonly ComboBox countdownBox = new ComboBox { Width = 70, Margin = new Thickness(0, 8, 8, 8) };

        // кольцо камер (S13): кнопки ракурсов по окружности вокруг «танцора», c05 внизу (фронтальный), шаг 40°;
        // позиция кнопки на кольце = ракурс камеры, недоступные камеры (нет файла) не рисуются
        private const double CameraYawStepDeg = 40.0;

        /// <summary>
        /// Знак азимутов сцены: -1, подобран по данным. Реальные относительные азимуты AIST++ (setting7_1):
        /// c01 +180, c02 +135, c03 +92, c04 +42, c06 -45, c07 -89, c08 -135 — знак -1 даёт yaw = +40..+160 для
        /// c01..c04 и -40..-160 для c06..c09, совпадающие с видео (сверка формы скелета с keypoints2d; знак +1
        /// расходится по всем камерам).
        /// </summary>
        private const double CameraYawSign = -1.0;
        private readonly Canvas angleRing = new Canvas { Width = 150, Height = 110, Margin = new Thickness(14, 2, 16, 2), VerticalAlignment = VerticalAlignment.Center };
        private readonly List<Button> ringButtons = new List<Button>();
        private string? currentCamera;
        private readonly TextBlock speedLabel = new TextBlock { Foreground = Text, Text = "Скорость", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 8, 4, 8) };
        private readonly Slider speedSlider = new Slider { Width = 120, Minimum = 0.2, Maximum = 2.0, TickFrequency = 0.1, IsSnapToTickEnabled = true, SmallChange = 0.1, Value = 1.0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 8) };
        private readonly TextBlock speedValue = new TextBlock { Foreground = Text, Text = "x1.0", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 8) };

        // регулятор кадра (S21, T11): множитель поверх авто-зума видео (T12: авто-кадр по bbox танцора)
        // и зума сцены призраков; умолчание 1.0 (чистый авто-кадр); один слайдер меняет оба сразу (S19)
        private readonly TextBlock zoomLabel = new TextBlock { Foreground = Text, Text = "Кадр", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 8, 4, 8) };
        private readonly Slider zoomSlider = new Slider { Width = 120, Minimum = 1.0, Maximum = 3.0, TickFrequency = 0.1, IsSnapToTickEnabled = true, SmallChange = 0.1, Value = 1.0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 8) };
        private readonly TextBlock zoomValue = new TextBlock { Foreground = Text, Text = "x1.0 × авто", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 8) };
        private readonly TextBlock countdownText = new TextBlock
        {
            Foreground = Text,
            Background = new SolidColorBrush(Color.FromArgb(0xb0, 0x10, 0x11, 0x14)),
            FontSize = 110,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        private readonly DispatcherTimer countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private LiveRecorder? recorder;
        private readonly TextBlock status = new TextBlock { Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ScaleTransform videoZoom = new ScaleTransform(1.0, 1.0); // зум кадра видео (S21), ведёт авто-кадр × слайдер на тике
        private readonly TranslateTransform videoPan = new TranslateTransform(); // авто-панорама видео (S21, T12), ведёт следящий кадр
        private readonly MediaElement media = new MediaElement { Stretch = Stretch.Uniform, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };

        /// <summary>Панель видео: видимое окно следящего кадра (высота окна для клампа и центра панорамы).</summary>
        private readonly Grid videoPanel = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0b, 0x0e)), Margin = new Thickness(10, 0, 5, 10), ClipToBounds = true };
        private readonly GhostCanvas ghost = new GhostCanvas();
        private readonly Grid views = new Grid();
        private readonly Grid sceneView = new Grid { Visibility = Visibility.Collapsed };

        // строка «поза: NN%» в HUD паузы (S22): на 6 пунктов крупнее обычных строк (базовый FontSize 15)
        private const double PoseLineFontSize = 21.0;
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

        // следящий кадр (S21, T12): <seq>.cams.json — камера -> кадры [t, u, v, uMin, vMin, uMax, vMax]:
        // root (u,v) и bbox танцора в пикселях кадра, t в секундах
        private const double AutoZoomFallback = 1.6; // база авто-зума без cams.json (прежнее умолчание)
        private const double BboxWindowSec = 0.7;    // расширение bbox по времени (гладкость, без пампинга на махах)
        private const double ShoePadRows = 75.0;     // обувь ниже лодыжек-ключевых точек, строк источника (худший кадр c05: выпад ~66)
        private const double HeadShiftFrac = 0.07;   // сдвиг центра окна вверх (запас голове), доли высоты окна
        private readonly Dictionary<string, double[][]> camRoot = new Dictionary<string, double[][]>();
        private double smoothedZoomAuto = AutoZoomFallback;
        private double smoothedCenterU = double.NaN;
        private double smoothedCenterV = double.NaN;
        private double smoothedPanX;
        private double smoothedPanY;
        // пробные пороги оценки (DeadZone 0.35, Saturation 1.0, MinCoverageBones 4): один экземпляр
        // и для скорера, и для гейта подсветки/процента позы — значения не расходятся
        private readonly TrialParams trialParams = new TrialParams();
        private TrialScorer? scorer;
        private TrialScore? lastScore;
        private PlayerSession? session;
        private AppMode mode = AppMode.Probe;
        private bool busy;
        private bool wantRunning;
        private bool paused;
        private bool mediaOpened;
        private bool videoOk;
        private int tick;
        private double lastScoredT = double.NaN;
        private DateTime? lastUserSeen;
        private double livePose01;   // мгновенный процент позы для HUD паузы (S22), пересчёт каждый кадр
        private bool livePoseValid;  // гейт покрытия для «поза: NN%» (T14/P0.4): ниже — «поза: —»
        private GhostLengths ghostLen = GhostLengths.FromLeg(0.8);

        // калибровка: медианы длин костей по первым секундам живого скелета, потом фиксированы
        private const double CalibrationSeconds = 5.0;

        // пол сцены (S15): высоты лодыжек ближайшего пользователя, скользящее окно, оценка — 2-й перцентиль
        private const int FloorSampleCap = 300;
        private const int FloorMinSamples = 30;
        private const int FloorRecalcTicks = 30; // пересчёт не чаще раза в секунду (тики по 33 мс)
        private static readonly string[][] AnkleNames =
        {
            new[] { "AnkleLeft", "LeftAnkle", "ankle_l" },
            new[] { "AnkleRight", "RightAnkle", "ankle_r" },
        };
        private readonly List<double> floorAnkleHeights = new List<double>();
        private double floorYm;
        private bool floorReady;
        private long lastFloorSampleUs = -1;
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

        // управление воспроизведением (S12-S14, S17, S19)
        private string? currentVideoFile;
        private TimeSpan? pendingRestore;
        private bool pendingResume;
        private double speedFactor = 1.0;
        private int countdownRemaining;
        private bool switchingCamera;
        private double lastVideoSeconds;

        /// <summary>Режимы одного окна (S16): проба с видео и оценкой либо сцена сенсора без видео.</summary>
        private enum AppMode
        {
            Probe,
            Scene,
        }

        /// <summary>
        /// Мини-часы на случай, когда видео не нашлось: призраки всё равно двигаются по реальному времени.
        /// Прошедшее время умножается на коэффициент скорости (S17), смена коэффициента не даёт скачка.
        /// </summary>
        private sealed class StopwatchLike
        {
            private readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            private double accumulated;
            private double lastMark;
            private double rate = 1.0;

            public double TotalSeconds => accumulated + ((sw.Elapsed.TotalSeconds - lastMark) * rate);

            public void SetRate(double newRate)
            {
                var now = sw.Elapsed.TotalSeconds;
                accumulated += (now - lastMark) * rate;
                lastMark = now;
                rate = newRate;
            }

            public void Restart()
            {
                sw.Restart();
                accumulated = 0;
                lastMark = 0;
            }
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
            modeBox.Items.Add("Проба");
            modeBox.Items.Add("Сцена сенсора");
            modeBox.SelectedIndex = 0;
            modeBox.SelectionChanged += (s, e) => OnModeChanged();

            countdownBox.Items.Add("3");
            countdownBox.Items.Add("4");
            countdownBox.Items.Add("5");
            countdownBox.SelectedIndex = 2;
            playButton.Click += (s, e) => TogglePlay();
            restartButton.Click += (s, e) => RestartFromZero();
            attemptButton.Click += (s, e) => StartAttempt();
            speedSlider.ValueChanged += (s, e) => OnSpeedChanged();
            zoomSlider.ValueChanged += (s, e) => OnZoomChanged();
            countdownTimer.Tick += (s, e) => CountdownTick();

            // стартовый множитель кадра (S21): --zoom либо умолчание слайдера 1.0 (ValueChanged на инициализатор не срабатывает)
            if (opts.Zoom.HasValue)
            {
                zoomSlider.Value = opts.Zoom.Value;
            }

            OnZoomChanged();

            // метка танцора в центре кольца камер (S13); Ellipse полным именем: Path из Shapes конфликтует с System.IO
            var dancerDot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Text };
            Canvas.SetLeft(dancerDot, 69);
            Canvas.SetTop(dancerDot, 38);
            var dancerLabel = new TextBlock { Text = "танцор", Foreground = Text, FontSize = 10, Width = 150, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(dancerLabel, 0);
            Canvas.SetTop(dancerLabel, 54);
            angleRing.Children.Add(dancerDot);
            angleRing.Children.Add(dancerLabel);

            var bar = new DockPanel { Background = Panel, LastChildFill = false };
            DockPanel.SetDock(status, Dock.Right);
            bar.Children.Add(status);
            bar.Children.Add(modeLabel);
            bar.Children.Add(modeBox);
            bar.Children.Add(trackerBox);
            bar.Children.Add(sensorButton);
            bar.Children.Add(recordButton);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Background = Panel };
            controls.Children.Add(playButton);
            controls.Children.Add(restartButton);
            controls.Children.Add(attemptButton);
            controls.Children.Add(countdownLabel);
            controls.Children.Add(countdownBox);
            controls.Children.Add(angleRing);
            controls.Children.Add(speedLabel);
            controls.Children.Add(speedSlider);
            controls.Children.Add(speedValue);
            controls.Children.Add(zoomLabel);
            controls.Children.Add(zoomSlider);
            controls.Children.Add(zoomValue);
            var bars = new StackPanel { Background = Panel };
            bars.Children.Add(bar);
            bars.Children.Add(controls);

            media.MediaOpened += (s, e) =>
            {
                mediaOpened = true;
                media.SpeedRatio = speedFactor;
                if (pendingRestore.HasValue)
                {
                    // переключение ракурса (S13): восстановить позицию и прежнее состояние паузы, не автоплей с нуля
                    media.Position = pendingRestore.Value;
                    pendingRestore = null;
                    switchingCamera = false; // часы призраков снова идут по позиции видео (S19)
                    var resume = pendingResume;
                    pendingResume = false;
                    paused = !resume;
                    if (resume)
                    {
                        media.Play();
                    }
                    else
                    {
                        media.Pause();
                    }
                }
                else
                {
                    paused = false;
                }

                UpdatePlayButton();
            };
            media.MediaFailed += (s, e) =>
            {
                switchingCamera = false;
                status.Text = "Видео не открылось: " + e.ErrorException.Message;
            };
            media.MediaEnded += (s, e) =>
            {
                paused = true;
                UpdatePlayButton();
            };

            // зум кадра видео (S21, T12) + следящая панорама videoPan (горизонталь и вертикаль,
            // ведётся на тике по root2d из cams.json): центр трансформа (0.5, 0.5), панорама
            // доминирует над якорем. Вылезшие края срезает панель — у Grid ClipToBounds по
            // умолчанию false, включаем явно
            media.RenderTransformOrigin = new Point(0.5, 0.5);
            var videoTransform = new TransformGroup();
            videoTransform.Children.Add(videoZoom);
            videoTransform.Children.Add(videoPan);
            media.RenderTransform = videoTransform;
            videoPanel.Children.Add(media);

            var ghostPanel = new Grid { Margin = new Thickness(5, 0, 10, 10) };
            ghostPanel.Children.Add(ghost);
            ghostPanel.Children.Add(hud);
            ghostPanel.Children.Add(countdownText);

            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(videoPanel, 0);
            Grid.SetColumn(ghostPanel, 1);
            views.Children.Add(videoPanel);
            views.Children.Add(ghostPanel);

            // панель сцены сенсора (S16): два SceneCanvas и строка статистики снизу
            sceneView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sceneView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sceneView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sceneView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(sceneFront, 0);
            Grid.SetRow(sceneFront, 0);
            Grid.SetColumn(sceneTop, 1);
            Grid.SetRow(sceneTop, 0);
            Grid.SetColumnSpan(sceneStats, 2);
            Grid.SetRow(sceneStats, 1);
            Grid.SetColumnSpan(sceneNoSensor, 2);
            Grid.SetRow(sceneNoSensor, 0);
            sceneView.Children.Add(sceneFront);
            sceneView.Children.Add(sceneTop);
            sceneView.Children.Add(sceneStats);
            sceneView.Children.Add(sceneNoSensor);

            var main = new Grid();
            main.Children.Add(views);
            main.Children.Add(sceneView);

            var root = new DockPanel();
            DockPanel.SetDock(bars, Dock.Top);
            root.Children.Add(bars);
            root.Children.Add(main);
            Content = root;

            if (opts.Mode == "scene")
            {
                modeBox.SelectedIndex = 1; // стартовый режим --mode scene (S16), сразу включает панель сцены
            }

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
                scorer = new TrialScorer(track, trialParams);
                ghostLen = GhostLengths.FromLeg(track.LegLengthM);
                ghost.SetLengths(ghostLen);
            }
            catch (Exception ex)
            {
                status.Text = "Эталон не загрузился (" + canonicalPath + "): " + ex.Message;
            }

            // следящий кадр (S21, T12): пиксели корня танцора по камерам, рядом с canonical.json
            LoadCamRoot(Path.Combine(Path.GetDirectoryName(canonicalPath)!, opts.Seq + ".cams.json"));

            var videoPath = FindVideo(track?.Video);
            if (videoPath != null && opts.Camera != null)
            {
                // стартовый ракурс (--camera cXX, S13): сразу нужный файл, кольцо и сцена стартуют согласованно
                var name = Path.GetFileName(videoPath);
                var m = Regex.Match(name, "c\\d\\d");
                if (m.Success && m.Value != opts.Camera)
                {
                    var candidate = Path.Combine(Path.GetDirectoryName(videoPath)!, name.Remove(m.Index, m.Length).Insert(m.Index, opts.Camera));
                    if (File.Exists(candidate))
                    {
                        videoPath = candidate;
                    }
                }
            }

            if (videoPath != null)
            {
                videoOk = true;
                currentVideoFile = videoPath;
                PopulateAngles(videoPath);
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
            if (tick % FloorRecalcTicks == 0)
            {
                RecalcFloor(); // пол сцены (S15): не чаще раза в секунду
            }

            if (mode == AppMode.Scene)
            {
                UpdateScene();
                return;
            }

            if (track == null)
            {
                UpdateVideoFrame(ClockSeconds()); // следящий кадр не зависит от эталонного трека
                return;
            }

            var t = ClockSeconds();
            UpdateVideoFrame(t);
            ghost.SetReference(track.Sample(t));
            UpdateLive(t);
            if (tick % 6 == 0)
            {
                UpdateHud(t);
            }
        }

        /// <summary>
        /// Читает <seq>.cams.json (следящий кадр, S21/T12): по камерам кадры {t, root:[u,v],
        /// bbox:[uMin,vMin,uMax,vMax]} с t в секундах. Нет файла/камеры — авто-кадр работает
        /// в fallback (база 1.6, центр кадра).
        /// </summary>
        private void LoadCamRoot(string path)
        {
            camRoot.Clear();
            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("cameras", out var cams))
                {
                    foreach (var c in cams.EnumerateObject())
                    {
                        if (!c.Value.TryGetProperty("frames", out var rows) || rows.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        var list = new List<double[]>();
                        foreach (var p in rows.EnumerateArray())
                        {
                            if (p.ValueKind != JsonValueKind.Object ||
                                    !p.TryGetProperty("t", out var tEl) ||
                                    !p.TryGetProperty("root", out var rootEl) ||
                                    !p.TryGetProperty("bbox", out var bboxEl) ||
                                    rootEl.GetArrayLength() < 2 || bboxEl.GetArrayLength() < 4)
                            {
                                continue;
                            }

                            list.Add(new[]
                            {
                                tEl.GetDouble(),
                                rootEl[0].GetDouble(), rootEl[1].GetDouble(),
                                bboxEl[0].GetDouble(), bboxEl[1].GetDouble(),
                                bboxEl[2].GetDouble(), bboxEl[3].GetDouble(),
                            });
                        }

                        if (list.Count > 0)
                        {
                            camRoot[c.Name] = list.ToArray();
                        }
                    }
                }
                else
                {
                    status.Text = "cams.json без ключа cameras: авто-кадр в fallback (" + path + ").";
                }
            }
            catch (Exception ex)
            {
                status.Text = "cams.json не загрузился: авто-кадр в fallback (" + ex.Message + ").";
            }
        }

        /// <summary>
        /// Расширенный по времени bbox танцора: объединение bbox кадров текущей камеры в окне
        /// t ± BboxWindowSec (гладкость без пампинга на махах); false, если данных нет.
        /// </summary>
        private bool TryGetAutoFrame(double t, out (double UMin, double VMin, double UMax, double VMax) box)
        {
            box = default;
            if (!camRoot.TryGetValue(currentCamera ?? string.Empty, out var rows) || rows.Length == 0)
            {
                return false;
            }

            var lo = 0;
            var hi = rows.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (rows[mid][0] < t - BboxWindowSec)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            var uMin = double.MaxValue;
            var vMin = double.MaxValue;
            var uMax = double.MinValue;
            var vMax = double.MinValue;
            for (var i = lo; i < rows.Length && rows[i][0] <= t + BboxWindowSec; i++)
            {
                uMin = Math.Min(uMin, rows[i][3]);
                vMin = Math.Min(vMin, rows[i][4]);
                uMax = Math.Max(uMax, rows[i][5]);
                vMax = Math.Max(vMax, rows[i][6]);
            }

            if (uMin > uMax)
            {
                return false; // в окне не оказалось ни одного кадра (берём fallback)
            }

            box = (uMin, vMin, uMax, vMax);
            return true;
        }

        /// <summary>
        /// Следящий кадр (S21, T12): авто-зум и панорама видео по bbox танцора из cams.json текущей
        /// камеры. zAuto = clamp(min(winW/(bw+pad), winH/(bh+pad)), 1, 3), pad = 25% габарита:
        /// вертикальный паддинг асимметричный (верхней кромке 40%, нижней 60%), низ bbox расширен
        /// на ShoePadRows строк (обувь ниже лодыжек-ключевых точек), окно панели в пикселях
        /// источника = win/(s); итог z = zAuto * слайдер (сглаженные 0.2/тик). Окно центрируется
        /// на сглаженном центре паддингованного bbox со сдвигом вверх 7% высоты окна (запас голове):
        /// Tx = -z*s*(cu - W/2), Ty = -z*s*(cv_цель - H/2). Клампы панорамы: окно не выходит за края
        /// контента. Нет cams.json — fallback: база zAuto = 1.6, центр = центр кадра (панорамы нет).
        /// </summary>
        private void UpdateVideoFrame(double t)
        {
            var sliderZoom = Math.Max(1.0, Math.Min(3.0, Math.Round(zoomSlider.Value, 1)));
            var winH = videoPanel.ActualHeight;
            if (!videoOk || !mediaOpened || media.NaturalVideoWidth <= 0 ||
                    media.ActualWidth < 10 || media.ActualHeight < 10 || winH < 10)
            {
                smoothedPanX = 0;
                smoothedPanY = 0;
                smoothedZoomAuto = AutoZoomFallback * sliderZoom;
                videoPan.X = 0;
                videoPan.Y = 0;
                return;
            }

            var wSrc = (double)media.NaturalVideoWidth;
            var hSrc = (double)media.NaturalVideoHeight;
            var s = Math.Min(media.ActualWidth / wSrc, media.ActualHeight / hSrc); // контент в пикселях элемента
            var winW = media.ActualWidth; // панель ширины контента (медиа заполняет окно по ширине)

            // авто-кадр: расширенный bbox, цель зума по габариту танцора с запасом pad = 25%
            var hasBox = TryGetAutoFrame(t, out var box);
            double zAutoTarget;
            double cUTarget;
            double cVTarget;
            if (hasBox)
            {
                var vMin = box.VMin;
                var vMax = box.VMax + ShoePadRows; // bbox по лодыжкам: обувь ниже, расширяем низ
                var bw = (box.UMax - box.UMin) * 1.25;                    // паддинг 25%: по половине на борт
                var bh = (vMax - vMin) * 1.25;                            // сверху 40% паддинга, снизу 60%
                zAutoTarget = Math.Max(1.0, Math.Min(3.0, Math.Min((winW / s) / bw, (winH / s) / bh)));
                cUTarget = (box.UMin + box.UMax) / 2.0;
                // центр паддингованного бокса: [vMin-0.4p, vMax+0.6p] -> центр = центр bbox + 0.1*p
                cVTarget = (vMin + vMax) / 2.0 + (0.10 * 0.25 * (vMax - vMin));
            }
            else
            {
                zAutoTarget = AutoZoomFallback;
                cUTarget = wSrc / 2.0;
                cVTarget = hSrc / 2.0;
            }

            if (double.IsNaN(smoothedCenterU))
            {
                smoothedCenterU = cUTarget; // первый валидный тик: без проползания из нуля
                smoothedCenterV = cVTarget;
            }
            else
            {
                smoothedCenterU += 0.2 * (cUTarget - smoothedCenterU);
                smoothedCenterV += 0.2 * (cVTarget - smoothedCenterV);
            }

            smoothedZoomAuto += 0.2 * (zAutoTarget - smoothedZoomAuto);
            var z = Math.Max(1.0, Math.Min(3.0, smoothedZoomAuto * sliderZoom));
            videoZoom.ScaleX = z;
            videoZoom.ScaleY = z;

            // панорама: окно центрируется на паддингованном bbox со сдвигом вверх 7% высоты окна
            var maxPanX = Math.Max(0.0, ((z * s * wSrc) - winW) / 2.0);
            var maxPanY = Math.Max(0.0, ((z * s * hSrc) - winH) / 2.0);
            var vTarget = smoothedCenterV - (HeadShiftFrac * winH / (s * z));
            var targetX = Math.Max(-maxPanX, Math.Min(maxPanX, -z * s * (smoothedCenterU - (wSrc / 2.0))));
            var targetY = Math.Max(-maxPanY, Math.Min(maxPanY, -z * s * (vTarget - (hSrc / 2.0))));

            smoothedPanX += 0.2 * (targetX - smoothedPanX);
            smoothedPanY += 0.2 * (targetY - smoothedPanY);
            videoPan.X = smoothedPanX;
            videoPan.Y = smoothedPanY;
        }

        /// <summary>Живой пользователь: ближайший к сенсору (минимум |z| корня), калибровка, канонизация, скоринг,
        /// краска костей с гейтом покрытия, мгновенный процент позы для паузы (S22).</summary>
        private void UpdateLive(double t)
        {
            var s = session;
            if (opts.NoSensor || s == null)
            {
                ResetUserDisplay();
                return;
            }

            var frame = s.Latest;
            if (frame == null || frame.Users.Count == 0)
            {
                ResetUserDisplay();
                return;
            }

            var userId = NearestUserId(frame, s.TrackerId);
            if (userId < 0 || !s.TryGetUser(userId, out var pose, out var lengths))
            {
                ResetUserDisplay();
                return;
            }

            CollectCalibration(lengths, frame.DeviceTimeUs);
            CollectFloorSample(pose, frame.DeviceTimeUs);
            var legM = calibrated && userLegM > 0 ? userLegM : track!.LegLengthM;
            var userFrame = Canonicalizer.ToCanonical(pose, userBones, legM, floorYm);

            // скоринг по часам видео, раз на новый момент времени (пауза не копит историю)
            if (scorer != null && !paused && !(Math.Abs(t - lastScoredT) < 1e-9))
            {
                lastScore = scorer.Update(t, userFrame);
                lastScoredT = t;
            }

            // краска костей: сравнение на применённом сдвиге скорера; на паузе сдвиг заморожен
            // (новый поиск не делается, S22) — эталон сэмплируется на застывший момент t - shift
            var shift = lastScore?.ShiftSec ?? 0;
            var comparison = TrialComparer.Compare(track!.Sample(t - shift), userFrame);

            // гейт подсветки (T14/P0.4, хвост E12): валидных костей меньше MinCoverageBones — кадр
            // без краски, призрак пользователя остаётся нейтральным
            var covered = CoverageOk(comparison);
            ghost.SetUser(userFrame, covered ? comparison.BoneDeviationRad : null);

            // мгновенный процент позы (S22): та же кривая мёртвой зоны/насыщения, что у скорера,
            // но по текущему кадру без окна истории; живёт только для отображения в HUD паузы
            livePose01 = PoseScore01(comparison.MeanDeviationRad);
            livePoseValid = covered;

            if (comparison.Coverage > 0)
            {
                lastUserSeen = DateTime.UtcNow;
            }
        }

        /// <summary>Пользователя нет: без призрака и без процента позы в HUD паузы.</summary>
        private void ResetUserDisplay()
        {
            ghost.SetUser(null, null);
            livePose01 = 0;
            livePoseValid = false;
        }

        /// <summary>Гейт покрытия (T14/P0.4): валидных костей в сравнении не меньше MinCoverageBones.</summary>
        private bool CoverageOk(TrialComparison comparison) =>
            (int)Math.Round(comparison.Coverage * CanonicalFrame.BoneCount) >= trialParams.MinCoverageBones;

        /// <summary>Кривая мёртвой зоны/насыщения скорера (TrialParams) на одном среднем отклонении:
        /// 1 - clamp((clamp(mean, 0, Saturation) - DeadZone) / (Saturation - DeadZone), 0, 1) (S22).</summary>
        private double PoseScore01(double meanDevRad)
        {
            var dead = trialParams.DeadZone;
            var sat = trialParams.Saturation;
            var span = Math.Max(sat - dead, 1e-9);
            var clamped = Math.Max(0.0, Math.Min(meanDevRad, sat));
            var x = Math.Max(0.0, Math.Min((clamped - dead) / span, 1.0));
            return 1.0 - x;
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
            var floorLine = FloorHudLine(inv); // S15: оценка пола или «оцениваю»
            if (opts.NoSensor || session == null)
            {
                hud.Text = "без сенсора" + Environment.NewLine + timeLine + Environment.NewLine + floorLine;
                return;
            }

            var seen = lastUserSeen.HasValue && (DateTime.UtcNow - lastUserSeen.Value).TotalSeconds <= 1.0;
            var score = lastScore;
            if (!seen || score == null)
            {
                hud.Text = "не вижу" + Environment.NewLine + timeLine + Environment.NewLine + floorLine;
                return;
            }

            if (paused)
            {
                // S22: пауза — режим подгонки позы. Бегущий процент заменён крупной строкой мгновенного
                // процента позы по текущему кадру (сдвиг заморожен с момента паузы); ниже гейта покрытия —
                // «поза: —». Остальные строки (итог, грубые, время, пол) остаются на замороженных значениях.
                var poseText = livePoseValid ? (livePose01 * 100).ToString("0", inv) + "%" : "—";
                hud.Inlines.Clear();
                hud.Inlines.Add(new Run("поза: " + poseText + Environment.NewLine) { FontSize = PoseLineFontSize });
                hud.Inlines.Add(new Run("Итог: " + (score.Total01 * 100).ToString("0", inv) + "%" + Environment.NewLine));
                hud.Inlines.Add(new Run("Грубые: " + score.Errors.ToString(inv) + Environment.NewLine));
                hud.Inlines.Add(new Run(timeLine + Environment.NewLine));
                hud.Inlines.Add(new Run(floorLine));
                return;
            }

            hud.Text = "Бегущий: " + (score.Running01 * 100).ToString("0", inv) + "%   Итог: " + (score.Total01 * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Грубые: " + score.Errors.ToString(inv) + "   Покрытие: " + (score.Coverage * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Сдвиг: " + score.ShiftSec.ToString("0.00", inv) + " с   " + timeLine + Environment.NewLine
                + floorLine;
        }

        /// <summary>Пол сцены (S15): за кадр — минимум высот валидных лодыжек ближайшего пользователя, окно до 300 значений.</summary>
        private void CollectFloorSample(ReconstructedPose pose, long deviceTimeUs)
        {
            if (deviceTimeUs == lastFloorSampleUs)
            {
                return;
            }

            lastFloorSampleUs = deviceTimeUs;
            var best = double.MaxValue;
            foreach (var aliases in AnkleNames)
            {
                ReconstructedJoint? ankle = null;
                foreach (var name in aliases)
                {
                    ankle = pose.Find(name);
                    if (ankle != null)
                    {
                        break;
                    }
                }

                if (ankle != null && ankle.Origin != JointOrigin.Invalid && ankle.Y < best)
                {
                    best = ankle.Y;
                }
            }

            if (best == double.MaxValue)
            {
                return;
            }

            if (floorAnkleHeights.Count >= FloorSampleCap)
            {
                floorAnkleHeights.RemoveAt(0);
            }

            floorAnkleHeights.Add(best);
        }

        /// <summary>Оценка пола (S15): 2-й перцентиль окна лодыжек; пока меньше 30 значений — floorYm = 0.</summary>
        private void RecalcFloor()
        {
            if (floorAnkleHeights.Count < FloorMinSamples)
            {
                floorReady = false;
                floorYm = 0;
                return;
            }

            var sorted = new List<double>(floorAnkleHeights);
            sorted.Sort();
            var idx = (int)Math.Ceiling(sorted.Count * 0.02) - 1;
            idx = Math.Max(0, Math.Min(sorted.Count - 1, idx));
            floorYm = sorted[idx];
            floorReady = true;
        }

        private string FloorHudLine(CultureInfo inv) =>
            floorReady ? "пол: " + floorYm.ToString("0.00", inv) + " м" : "пол: оцениваю";

        /// <summary>Режим «Сцена сенсора» (S16): скелеты всех людей в оба вида, статистика потока; без сессии — «сенсор не запущен».</summary>
        private void UpdateScene()
        {
            var s = session;
            var users = new List<(int id, ReconstructedPose pose)>();
            var nearest = -1;
            var rootY = double.NaN;
            if (s != null)
            {
                var frame = s.Latest;
                if (frame != null)
                {
                    nearest = NearestUserId(frame, s.TrackerId);
                    foreach (var u in frame.Users)
                    {
                        if (!s.TryGetUser(u.Id, out var pose, out _))
                        {
                            continue;
                        }

                        users.Add((u.Id, pose));
                        if (u.Id == nearest)
                        {
                            CollectFloorSample(pose, frame.DeviceTimeUs);
                            rootY = RootHeight(pose);
                        }
                    }
                }
            }

            var trackerId = s?.TrackerId ?? Topology.Kinect;
            sceneFront.TrackerId = trackerId;
            sceneTop.TrackerId = trackerId;
            sceneFront.SetUsers(users);
            sceneTop.SetUsers(users);
            sceneFront.SetFloor(floorYm, floorReady);
            if (tick % 6 == 0)
            {
                var inv = CultureInfo.InvariantCulture;
                sceneStats.Text = "скелет: " + (s?.SkeletonFps ?? 0).ToString("0.0", inv) + " fps" + Environment.NewLine
                    + "людей в кадре: " + users.Count.ToString(inv) + Environment.NewLine
                    + "ближайший: " + (nearest >= 0 ? "id " + nearest.ToString(inv) : "—") + Environment.NewLine
                    + FloorHudLine(inv) + Environment.NewLine
                    + "корень ближайшего: " + (double.IsNaN(rootY) ? "—" : rootY.ToString("0.00", inv) + " м");
                sceneNoSensor.Visibility = s == null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Высота корня (таз) над нулём сенсора, м; NaN, если корень недоступен.</summary>
        private static double RootHeight(ReconstructedPose pose)
        {
            foreach (var name in new[] { "SpineBase", "Waist", "root" })
            {
                var j = pose.Find(name);
                if (j != null && j.Origin != JointOrigin.Invalid)
                {
                    return j.Y;
                }
            }

            return double.NaN;
        }

        /// <summary>Переключение режима мгновенное (S16): сессия сенсора не трогается, видео продолжает играть.</summary>
        private void OnModeChanged()
        {
            var scene = modeBox.SelectedIndex == 1;
            if (scene == (mode == AppMode.Scene))
            {
                return;
            }

            mode = scene ? AppMode.Scene : AppMode.Probe;
            views.Visibility = scene ? Visibility.Collapsed : Visibility.Visible;
            sceneView.Visibility = scene ? Visibility.Visible : Visibility.Collapsed;
            status.Text = scene
                ? "Режим сцены сенсора: скелеты всех людей, вид спереди и сверху."
                : "Режим пробы: " + opts.Seq + ".";
        }

        /// <summary>
        /// Мастер-часы (S1): позиция видео; при переключении ракурса (S19) позиция держится на последнем
        /// значении, чтобы призраки не прыгали на ноль, пока MediaElement перезагружает файл.
        /// </summary>
        private double ClockSeconds()
        {
            if (!mediaOpened || !videoOk)
            {
                return fallbackClock.TotalSeconds;
            }

            if (switchingCamera)
            {
                return lastVideoSeconds;
            }

            lastVideoSeconds = media.Position.TotalSeconds;
            return lastVideoSeconds;
        }

        private void TogglePlay()
        {
            if (!mediaOpened)
            {
                return;
            }

            if (paused)
            {
                // после MediaEnded Play() продолжает с конца и сразу кончается: начинаем с нуля
                if (media.NaturalDuration.HasTimeSpan && media.Position >= media.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(200))
                {
                    RestartFromZero();
                    return;
                }

                media.Play();
                paused = false;
            }
            else
            {
                media.Pause();
                paused = true;
            }

            UpdatePlayButton();
        }

        /// <summary>Текст кнопки плей/пауза всегда синхронен состоянию (кнопка и Space меняют одно и то же).</summary>
        private void UpdatePlayButton() => playButton.Content = paused ? "▶ Плей" : "⏸ Пауза";

        /// <summary>Попытка (S14): пауза видео и обратный отсчёт 3-5 с поверх сцены; по нулю рестарт с нуля (скорер тоже сбрасывается).</summary>
        private void StartAttempt()
        {
            var idx = countdownBox.SelectedIndex;
            countdownRemaining = idx == 0 ? 3 : idx == 1 ? 4 : 5;
            countdownText.Text = countdownRemaining.ToString(CultureInfo.InvariantCulture);
            countdownText.Visibility = Visibility.Visible;
            if (mediaOpened && !paused)
            {
                media.Pause();
                paused = true;
                UpdatePlayButton();
            }

            // повторный клик во время отсчёта перезапускает отсчёт
            countdownTimer.Stop();
            countdownTimer.Start();
        }

        private void CountdownTick()
        {
            countdownRemaining--;
            if (countdownRemaining > 0)
            {
                countdownText.Text = countdownRemaining.ToString(CultureInfo.InvariantCulture);
                return;
            }

            countdownTimer.Stop();
            countdownText.Visibility = Visibility.Collapsed;
            RestartFromZero();
        }

        /// <summary>Ракурсы (S13): кольцо кнопок c01..c09 вокруг «танцора», только камеры с существующим файлом.</summary>
        private void PopulateAngles(string videoPath)
        {
            foreach (var b in ringButtons)
            {
                angleRing.Children.Remove(b);
            }

            ringButtons.Clear();
            var name = Path.GetFileName(videoPath);
            var dir = Path.GetDirectoryName(videoPath);
            var m = Regex.Match(name, "c\\d\\d");
            if (!m.Success || string.IsNullOrEmpty(dir))
            {
                return;
            }

            currentCamera = m.Value;
            for (var i = 1; i <= 9; i++)
            {
                var cam = "c" + i.ToString("00", CultureInfo.InvariantCulture);
                var candidate = name.Remove(m.Index, m.Length).Insert(m.Index, cam);
                if (!File.Exists(Path.Combine(dir, candidate)))
                {
                    continue; // недоступная камера не рисуется вовсе
                }

                // позиция на кольце = ракурс: c05 внизу (фронтальный), соседи по 40°, c01 почти напротив;
                // тот же знак, что у азимута сцены: карта кольца согласована с поворотом 3D-сцены
                var a = (90 + (CameraYawSign * ((i - 5) * CameraYawStepDeg))) * Math.PI / 180.0;
                var button = new Button
                {
                    Content = cam,
                    Width = 30,
                    Height = 30,
                    FontSize = 10,
                    Padding = new Thickness(0),
                    Tag = cam,
                    Background = cam == currentCamera ? RingActive : RingIdle,
                    Foreground = Text,
                };
                Canvas.SetLeft(button, 75 + (55 * Math.Cos(a)) - 15);
                Canvas.SetTop(button, 52 + (35 * Math.Sin(a)) - 15);
                button.Click += (s, e) => SwitchAngle(cam);
                angleRing.Children.Add(button);
                ringButtons.Add(button);
            }

            ghost.CameraYawRad = YawRad(currentCamera);
        }

        /// <summary>Азимут камеры для 3D-сцены (S19): c05 = 0°, шаг 40° по номеру; знак согласован с видео на снимках E4.</summary>
        private static double YawRad(string cam)
        {
            var n = int.Parse(cam.Substring(1), CultureInfo.InvariantCulture);
            return CameraYawSign * ((n - 5) * CameraYawStepDeg) * Math.PI / 180.0;
        }

        /// <summary>
        /// Переключение ракурса (S13, S19): одна кнопка меняет видео и камеру 3D-сцены; позиция и состояние
        /// паузы сохраняются и восстанавливаются в MediaOpened, призраки идут по тем же часам без рывка.
        /// </summary>
        private void SwitchAngle(string cam)
        {
            if (!videoOk || currentVideoFile == null || string.IsNullOrEmpty(cam))
            {
                return;
            }

            var name = Path.GetFileName(currentVideoFile);
            var m = Regex.Match(name, "c\\d\\d");
            if (!m.Success || m.Value == cam)
            {
                return;
            }

            var dir = Path.GetDirectoryName(currentVideoFile);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var newPath = Path.Combine(dir, name.Remove(m.Index, m.Length).Insert(m.Index, cam));
            if (!File.Exists(newPath))
            {
                return;
            }

            pendingRestore = mediaOpened ? media.Position : TimeSpan.Zero;
            pendingResume = !paused;
            switchingCamera = true;
            currentVideoFile = newPath;
            currentCamera = cam;
            ghost.CameraYawRad = YawRad(cam); // сцена поворачивается вместе с видео
            foreach (var b in ringButtons)
            {
                b.Background = (string)b.Tag == cam ? RingActive : RingIdle;
            }

            media.Source = new Uri(newPath);
            if (pendingResume)
            {
                media.Play();
            }

            status.Text = "Ракурс " + cam + ", позиция " + pendingRestore.Value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с сохраняется...";
        }

        /// <summary>Скорость (S17): SpeedRatio видео и коэффициент резервных часов, действует и во время воспроизведения.</summary>
        private void OnSpeedChanged()
        {
            var v = Math.Round(speedSlider.Value, 1);
            if (v < 0.2)
            {
                v = 0.2;
            }

            if (v > 2.0)
            {
                v = 2.0;
            }

            speedFactor = v;
            speedValue.Text = "x" + v.ToString("0.0", CultureInfo.InvariantCulture);
            if (mediaOpened)
            {
                media.SpeedRatio = v;
            }

            fallbackClock.SetRate(v);
        }

        /// <summary>
        /// Кадр (S21, S19, T12): слайдер — множитель поверх авто-зума видео (итог ведёт авто-кадр
        /// на тике, UpdateVideoFrame) и прямой зум сцены призраков (GhostCanvas.FrameZoom).
        /// </summary>
        private void OnZoomChanged()
        {
            var v = Math.Round(zoomSlider.Value, 1);
            if (v < 1.0)
            {
                v = 1.0;
            }

            if (v > 3.0)
            {
                v = 3.0;
            }

            zoomValue.Text = "x" + v.ToString("0.0", CultureInfo.InvariantCulture) + " × авто";
            ghost.FrameZoom = v;
            // видео-зум здесь не ставится: итог z = zAuto * слайдер применяется на тике
        }

        private void RestartFromZero()
        {
            paused = false;
            lastScore = null;
            lastScoredT = double.NaN;
            if (track != null)
            {
                scorer = new TrialScorer(track, trialParams);
            }

            if (mediaOpened)
            {
                // WPF MediaElement: после MediaEnded рестарт требует Stop перед сбросом позиции, иначе Play() не начинает заново
                media.Stop();
                media.Position = TimeSpan.Zero;
                media.Play();
            }
            else
            {
                fallbackClock.Restart();
            }

            UpdatePlayButton();
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
            countdownTimer.Stop();
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
