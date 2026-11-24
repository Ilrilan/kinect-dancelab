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
using DanceLab.View;

namespace PlayerTrial
{
    /// <summary>
    /// Окно пробного плеера (specs/player/trial.md): слева видео эталонного танцора со звуком (MediaElement,
    /// часы пробы = Position), справа 3D-сцена двух призраков с HUD. Кольцо кнопок камер (S13) меняет ракурс
    /// видео и камеры сцены призраков одной кнопкой (S19). Управление: Space пауза/плей (пауза — режим
    /// подгонки позы, S22: эталон застыл, живая подсветка и мгновенный процент позы), R рестарт с нуля.
    /// Финальный экран итогов трека (T20): по MediaEnded с оценкой — итоговый процент, грубые ошибки,
    /// покрытие, длительность; «Ещё раз» = попытка, «Закрыть» прячет.
    /// Сторож триала (T19): nuitrack-сессия умерла (нет скелета дольше 5 с) — авто-рестарт Stop->Start,
    /// видео/оценка/эталон-призрак не останавливаются; калибровка ноги и пол сбрасываются только при
    /// смене трекера. Код-построенный UI по образцу tools/sensor_view.
    /// </summary>
    internal sealed class MainWindow : Window
    {
        /// <summary>Код возврата процесса тура (S34): 0 — PASS, 1 — FAIL; читает Program.Main.</summary>
        public static int TourExitCode { get; private set; }

        /// <summary>Столбцы tour.tsv (S34) ровно в порядке контракта.</summary>
        private static readonly string[] TourTsvColumns =
        {
            "seq", "source", "cam", "t", "zoom", "z", "zAutoTarget", "panX", "panY",
            "panAtLimitX", "panAtLimitY", "winW", "winH", "videoW", "videoH",
            "boxLeft", "boxTop", "boxRight", "boxBottom", "feetY", "boxCenterDx",
            "curLeft", "curTop", "curRight", "curBottom",
            "ghostLeft", "ghostTop", "ghostRight", "ghostBottom", "feetDy",
            "sceneOpaquePx", "curRootDx", "ghostRootDx",
        };

        private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x1b, 0x1c, 0x22));
        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xea));
        private static readonly Brush RingIdle = new SolidColorBrush(Color.FromRgb(0x2a, 0x2c, 0x34));

        /// <summary>Заливка активной кнопки камеры (primary, как LeftColor в SkeletonCanvas).</summary>
        private static readonly Brush RingActive = new SolidColorBrush(Color.FromRgb(0x42, 0xa5, 0xf5));

        /// <summary>Тёмная подпись на выбранной (синей) кнопке камеры (S24): тёмный текст на синем.</summary>
        private static readonly Brush RingActiveText = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14));

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
            Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1c, 0x22)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            FontWeight = FontWeights.Normal,
            LineHeight = 24,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(10, 0, 10, 10),
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
        private readonly Button playButton = new Button { Content = "⏸ Пауза", Width = 100, FontSize = 20, Margin = new Thickness(10, 8, 8, 8) };
        private readonly Button restartButton = new Button { Content = "⟳ Рестарт", Width = 100, FontSize = 20, Margin = new Thickness(0, 8, 8, 8) };
        private readonly Button attemptButton = new Button { Content = "Попытка", Width = 100, FontSize = 20, Margin = new Thickness(0, 8, 8, 8) };
        private readonly TextBlock countdownLabel = new TextBlock { Foreground = Text, Text = "Отсчёт:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 8, 4, 8) };
        // S24: поле отсчёта — высота обычного поля, а не ряда тулбара
        private readonly ComboBox countdownBox = new ComboBox { Width = 70, Height = 40, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 8) };

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
        private readonly Canvas angleRing = new Canvas { Width = CameraRing.CanvasW, Height = CameraRing.CanvasH, Margin = new Thickness(10, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
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

        // финальный экран итогов трека (T20, аудит P1.9): карточка по центру сцены призраков
        // (расположение как countdownText, панель шире), появляется при MediaEnded: с оценкой —
        // «Трек пройден» и итог; без оценки (lastScore null: --no-sensor, сенсор не видел танцора)
        // — карточка «Трек закончен / без оценки» с предложением повторить (S28), конец трека
        // не должен выглядеть как пауза. «Ещё раз» = попытка (отсчёт, рестарт с нуля), «Закрыть»
        // просто прячет; любой StartAttempt/RestartFromZero прячет, смена ракурса оверлей
        // сохраняет — финал можно рассматривать в другом ракурсе видео. Запись и сенсор оверлей не трогает.
        private static readonly Brush FinalCardBrush = new SolidColorBrush(Color.FromArgb(0xF0, 0x1b, 0x1c, 0x22));
        private readonly Border finalCard = new Border
        {
            Background = FinalCardBrush,
            BorderBrush = RingIdle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(36, 26, 36, 26),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock finalTitle = new TextBlock { Foreground = Text, Text = "Трек пройден", FontSize = 26, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        private readonly TextBlock finalTotal = new TextBlock
        {
            Foreground = Text,
            FontSize = 72,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        private readonly TextBlock finalStats = new TextBlock
        {
            Foreground = Text,
            FontSize = 18,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        private readonly Button finalRetryButton = new Button { Content = "Ещё раз", Width = 130, Height = 32, Margin = new Thickness(0, 0, 12, 0), Background = RingIdle, Foreground = Text };
        private readonly Button finalCloseButton = new Button { Content = "Закрыть", Width = 130, Height = 32, Background = RingIdle, Foreground = Text };
        private double finalDurationSec; // длительность трека, ClockSeconds на момент MediaEnded
        private double coverageSum;      // сумма покрытий оценённых кадров: среднее для финала (T20),
        private int coverageCount;       // т.к. TrialScore.Coverage — покрытие последнего сравнения, не среднее

        private LiveRecorder? recorder;
        private readonly TextBlock status = new TextBlock { Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), MaxWidth = 640, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ScaleTransform videoZoom = new ScaleTransform(1.0, 1.0); // зум кадра видео (S21), ведёт авто-кадр × слайдер на тике
        private readonly TranslateTransform videoPan = new TranslateTransform(); // авто-панорама видео (S21, T12), ведёт следящий кадр
        private readonly MediaElement media = new MediaElement { Stretch = Stretch.Uniform, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };

        /// <summary>Панель видео: видимое окно следящего кадра (высота окна для клампа и центра панорамы).</summary>
        private readonly Grid videoPanel = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0b, 0x0e)), Margin = new Thickness(10, 0, 5, 10), ClipToBounds = true };
        private readonly GhostCanvas ghost = new GhostCanvas();

        /// <summary>Панель сцены: призрак + HUD + оверлеи; её снимает тур (S34) вместе с HUD.</summary>
        private readonly Grid ghostPanel = new Grid { Margin = new Thickness(5, 0, 10, 10) };
        private readonly Grid views = new Grid();
        private readonly Grid sceneView = new Grid { Visibility = Visibility.Collapsed };

        // строка «поза: NN%» в HUD паузы (S22): на 6 пунктов крупнее обычных строк (базовый FontSize 15)
        private const double PoseLineFontSize = 21.0;
        private readonly TextBlock hud = new TextBlock
        {
            Foreground = Text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            IsHitTestVisible = false,
        };

        // подложка HUD сцены (S41): плотная плашка цвета фона — подписи «без сенсора»/«время»
        // читаются при любом зуме, призрак проходит под ней; в ghostPanel добавляется подложка,
        // содержимое правится по имени поля hud как раньше
        private readonly Border hudPlate = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0A, 0x0B, 0x0E)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(14, 14, 8, 8), // прежний (8) плюс 6 сверху и слева
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly StopwatchLike fallbackClock = new StopwatchLike();

        private CanonicalTrack? track;

        // следящий кадр (S21, T12 → T22, аудит P2.12): логика авто-кадра/панорамы и данные
        // <seq>.cams.json (камера → CamTrack: root/bbox по t) вынесены в DanceLab.View.FrameFollower;
        // окно на тике кормит его числами окна/видео/слайдера и применяет итог к трансформам
        private FrameFollower follower = new FrameFollower(new Dictionary<string, CamTrack>());

        /// <summary>Данные cams.json записи (камера → CamTrack), как у follower; тур (S34) берёт рамки cur* отсюда.</summary>
        private Dictionary<string, CamTrack> camTracks = new Dictionary<string, CamTrack>();
        // пробные пороги оценки (DeadZone 0.35, Saturation 1.0, MinCoverageBones 4): один экземпляр
        // и для скорера, и для гейта подсветки/процента позы — значения не расходятся
        private readonly TrialParams trialParams = new TrialParams();
        private TrialScorer? scorer;
        private TrialScore? lastScore;
        private PlayerSession? session;

        // разворот направления взгляда живой позы (S39): состояние через кадры; Reset при
        // рестарте с нуля и при пропаже пользователя (новое появление — направление заново)
        private readonly HeadingUnwrapper userHeading = new HeadingUnwrapper();

        private AppMode mode = AppMode.Probe;
        private bool busy;
        private bool wantRunning;

        // сторож сессии Nuitrack-триала (T19, аудит P1.8): триальная лицензия убивает сессию
        // каждые 3 минуты — скелетный поток замирает, призрак пользователя стоит
        private const double TrialDeadFps = 0.5;          // поток мёртв: SkeletonFps ниже порога
        private const double TrialDeadSeconds = 5.0;      // сколько секунд поток мёртв до рестарта
        private const double TrialStartupGraceSec = 20.0; // грейс на загрузку нейросети nuitrack-ai (Elapsed)
        private const int TrialRestartLimit = 10;         // перезапусков подряд, дальше — вручную
        private DateTime? trialDeadSince;                 // с какого момента скелетный поток мёртв
        private int trialRestarts;                        // перезапуски за запуск окна, сброс ручным Стоп/Старт
        private bool trialRestarting;                     // цикл Stop->Start идёт, сторож молчит
        private bool trialRestartLimitHit;                // лимит достигнут, ждём ручного включения
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

        // пол сцены (S15 + T17): валидный FloorY Kinect-кадра приоритетен, fallback — лодыжки
        // (2-й перцентиль высот, скользящее окно)
        private const int FloorSampleCap = 300;
        private const int FloorMinSamples = 30;
        private const int FloorRecalcTicks = 30; // пересчёт по лодыжкам не чаще раза в секунду (тики по 33 мс)
        private const double KinectFloorAlpha = 0.3; // сглаживание FloorY на тик (T17)
        private static readonly string[][] AnkleNames =
        {
            new[] { "AnkleLeft", "LeftAnkle", "ankle_l" },
            new[] { "AnkleRight", "RightAnkle", "ankle_r" },
        };
        private readonly List<double> floorAnkleHeights = new List<double>();
        private double ankleFloorYm;   // 2-й перцентиль лодыжек (S15)
        private bool ankleReady;
        private double floorYm;        // итоговый пол сцены: FloorY трекера или ankleFloorYm
        private FloorSource floorSource = FloorSource.Estimating;
        private long lastFloorSampleUs = -1;
        private readonly object floorYSync = new object();
        private double? rawFloorY; // FloorY последнего сырого кадра сенсора (T17)
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

        /// <summary>Окно другой записи тура --tour-set (T42): копия Options с заменённым Seq.</summary>
        public MainWindow(Options opts, string seq) : this(opts.WithSeq(seq))
        {
        }

        public MainWindow(Options options)
        {
            opts = options;
            Title = "DanceLab: пробный плеер (" + opts.Seq + ")";
            // окно целиком помещается в рабочую область экрана (S31): при масштабе 150% окно 1320x780 шире и выше экрана,
            // правый край уходил за экран, низ оказывался под панелью задач
            var workArea = SystemParameters.WorkArea; // DIPs, без панели задач
            Width = Math.Min(1320, workArea.Width);
            Height = Math.Min(780, workArea.Height);
            MaxWidth = Math.Max(320, workArea.Width);
            MaxHeight = Math.Max(240, workArea.Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14));

            // S23: window-level тёмный стиль кнопки — тулбар, кольцо камер и финальная карта;
            // наведение и нажатие не белят кнопку и не прячут подпись
            Resources.Add(typeof(Button), CreateDarkButtonStyle());

            // E32: тёмные ComboBox и ComboBoxItem как умолчания окна — тулбар целиком в одной
            // тёмной теме; стилизация свойствами не перекрашивает popup (шаблон Aero2 красит
            // его системными кистями), поэтому полный шаблон из XAML-строки
            Resources.Add(typeof(ComboBox), CreateDarkComboBoxStyle());
            Resources.Add(typeof(ComboBoxItem), CreateDarkComboBoxItemStyle());

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
            finalRetryButton.Click += (s, e) => StartAttempt(); // «Ещё раз» с финала (T20) = попытка
            finalCloseButton.Click += (s, e) => HideFinalOverlay();
            speedSlider.ValueChanged += (s, e) => OnSpeedChanged();
            zoomSlider.ValueChanged += (s, e) => OnZoomChanged();
            countdownTimer.Tick += (s, e) => CountdownTick();

            // стартовый множитель кадра (S21): --zoom либо умолчание слайдера 1.0 (ValueChanged на инициализатор не срабатывает)
            if (opts.Zoom.HasValue)
            {
                zoomSlider.Value = opts.Zoom.Value;
            }

            OnZoomChanged();

            // метка танцора в центре кольца камер (S13, S24: позиции из CameraRing);
            // Ellipse полным именем: Path из Shapes конфликтует с System.IO
            var dancerDot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Text };
            Canvas.SetLeft(dancerDot, CameraRing.CenterX - 6);
            Canvas.SetTop(dancerDot, CameraRing.CenterY - 14);
            var dancerLabel = new TextBlock { Text = "танцор", Foreground = Text, FontSize = 10, Width = CameraRing.CanvasW, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(dancerLabel, 0);
            Canvas.SetTop(dancerLabel, CameraRing.CenterY + 2);
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
                SetStatus("Видео не открылось: " + e.ErrorException.Message);
            };
            media.MediaEnded += (s, e) =>
            {
                paused = true;
                finalDurationSec = ClockSeconds(); // длительность трека на конец (T20)
                ShowFinalOverlay(); // с оценкой — итоги (T20), без оценки — карточка «Трек закончен» (S28)
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

            ghostPanel.Children.Add(ghost);

            // подложка получает текст HUD здесь: инициализатор поля не может ссылаться
            // на поле экземпляра hud
            hudPlate.Child = hud;
            ghostPanel.Children.Add(hudPlate);
            ghostPanel.Children.Add(countdownText);

            // финальный экран (T20): карточка итогов в центре сцены призраков, поверх остального
            var finalButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            finalButtons.Children.Add(finalRetryButton);
            finalButtons.Children.Add(finalCloseButton);
            var finalStack = new StackPanel();
            finalStack.Children.Add(finalTitle);
            finalStack.Children.Add(finalTotal);
            finalStack.Children.Add(finalStats);
            finalStack.Children.Add(finalButtons);
            finalCard.Child = finalStack;
            ghostPanel.Children.Add(finalCard);

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

            SetStatus("Загрузка...");
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

        /// <summary>
        /// Тёмный стиль кнопки (S23, аудит 2026-11-23): фон RingIdle и светлая подпись всегда;
        /// наведение и нажатие — светлая полупрозрачная вуаль поверх (hover Opacity 0.12,
        /// pressed 0.24), цвет фона и подпись триггеры не меняют — текст читаем под курсором.
        /// Отключённая кнопка тускнеет (bg Opacity 0.4). Шаблон построен кодом (FrameworkElementFactory),
        /// регистрируется как умолчание окна: Resources.Add(typeof(Button), ...).
        /// </summary>
        private static Style CreateDarkButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(BackgroundProperty, RingIdle));
            style.Setters.Add(new Setter(ForegroundProperty, Text));
            style.Setters.Add(new Setter(BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x3a, 0x3d, 0x47))));
            style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

            var grid = new FrameworkElementFactory(typeof(Grid));

            var bg = new FrameworkElementFactory(typeof(Border), "bg");
            bg.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bg.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bg.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            grid.AppendChild(bg);

            var hover = new FrameworkElementFactory(typeof(Border), "hover");
            hover.SetValue(Border.BackgroundProperty, Brushes.White);
            hover.SetValue(Border.OpacityProperty, 0.0);
            hover.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            hover.SetValue(Border.IsHitTestVisibleProperty, false);
            grid.AppendChild(hover);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = grid };

            // S23: сеттеры с TargetName допустимы только в Template.Triggers, не в Style.Triggers
            var over = new Trigger { Property = IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(OpacityProperty, 0.12, "hover"));
            template.Triggers.Add(over);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(OpacityProperty, 0.24, "hover"));
            template.Triggers.Add(pressed);

            var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(OpacityProperty, 0.4, "bg"));
            template.Triggers.Add(disabled);

            style.Setters.Add(new Setter(TemplateProperty, template));

            return style;
        }

        /// <summary>
        /// Тёмный стиль ComboBox (E32, 2026-11-23): полный шаблон по образцу Aero2 —
        /// стилизация одними свойствами не перекрашивает popup, шаблон Aero2 красит его
        /// системными кистями. XAML строится в коде (XamlReader.Parse, XAML-файлов в
        /// проекте нет). Части с x:Name обязательны — их ищет код ComboBox: templateRoot,
        /// toggleButton, contentPresenter, PART_Popup, dropDownBorder. Бокс #FF2A2C34,
        /// рамка #FF3A3D47; при наведении и открытом списке рамка #FF5FB3F8, фон не меняется;
        /// отключённый бокс Opacity 0.5. Список: dropDownBorder #FF1B1C22, радиус 4,
        /// отступ сверху 2. Регистрируется как умолчание окна: Resources.Add(typeof(ComboBox), ...).
        /// </summary>
        private static Style CreateDarkComboBoxStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ComboBox'>
  <Setter Property='Background' Value='#FF2A2C34' />
  <Setter Property='Foreground' Value='#FFE6E6EA' />
  <Setter Property='BorderBrush' Value='#FF3A3D47' />
  <Setter Property='BorderThickness' Value='1' />
  <Setter Property='Padding' Value='8,2,4,2' />
  <Setter Property='SnapsToDevicePixels' Value='True' />
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBox'>
        <Grid x:Name='templateRoot' SnapsToDevicePixels='True'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*' />
            <ColumnDefinition Width='32' />
          </Grid.ColumnDefinitions>
          <ToggleButton x:Name='toggleButton' Grid.ColumnSpan='2'
                        Background='{TemplateBinding Background}'
                        BorderBrush='{TemplateBinding BorderBrush}'
                        BorderThickness='{TemplateBinding BorderThickness}'
                        Focusable='False' ClickMode='Press'
                        IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
            <ToggleButton.Template>
              <ControlTemplate TargetType='ToggleButton'>
                <Border x:Name='border' Background='{TemplateBinding Background}'
                        BorderBrush='{TemplateBinding BorderBrush}'
                        BorderThickness='{TemplateBinding BorderThickness}'
                        CornerRadius='3' SnapsToDevicePixels='True'>
                  <Path Data='M 0 0 L 4 4 L 8 0 Z' Fill='{TemplateBinding Foreground}'
                        HorizontalAlignment='Right' VerticalAlignment='Center'
                        Margin='0,0,10,0' IsHitTestVisible='False' />
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='border' Property='BorderBrush' Value='#FF5FB3F8' />
                  </Trigger>
                  <Trigger Property='IsChecked' Value='True'>
                    <Setter TargetName='border' Property='BorderBrush' Value='#FF5FB3F8' />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter x:Name='contentPresenter' Grid.Column='0'
                            Content='{TemplateBinding SelectionBoxItem}'
                            ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                            ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'
                            ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'
                            Margin='{TemplateBinding Padding}'
                            HorizontalAlignment='Left' VerticalAlignment='Center'
                            IsHitTestVisible='False' />
          <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False'
                 IsOpen='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'
                 Placement='Bottom' PopupAnimation='Fade'>
            <Border x:Name='dropDownBorder' Background='#FF1B1C22' BorderBrush='#FF3A3D47'
                    BorderThickness='1' CornerRadius='4' Margin='0,2,0,0'
                    MaxHeight='{TemplateBinding MaxDropDownHeight}'
                    MinWidth='{Binding ActualWidth, ElementName=templateRoot}'
                    SnapsToDevicePixels='True'>
              <ScrollViewer Margin='2' VerticalScrollBarVisibility='Auto'
                            HorizontalScrollBarVisibility='Auto' SnapsToDevicePixels='True'>
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained' />
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter Property='Opacity' Value='0.5' />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        /// <summary>
        /// Тёмный стиль ComboBoxItem (E32, 2026-11-23): пункты выпадающего списка — фон
        /// #FF1B1C22, текст #FFE6E6EA; наведение #FF2F323C, выбран — #FF42A5F5 с тёмным
        /// текстом #FF101114, отключённый — Opacity 0.5. Шаблон: Border (Background и
        /// BorderBrush от шаблона) + ContentPresenter с отступом 10,6. Вторая XAML-строка,
        /// регистрируется как умолчание окна: Resources.Add(typeof(ComboBoxItem), ...).
        /// </summary>
        private static Style CreateDarkComboBoxItemStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ComboBoxItem'>
  <Setter Property='Background' Value='#FF1B1C22' />
  <Setter Property='Foreground' Value='#FFE6E6EA' />
  <Setter Property='SnapsToDevicePixels' Value='True' />
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBoxItem'>
        <Border x:Name='bd' Background='{TemplateBinding Background}'
                BorderBrush='{TemplateBinding BorderBrush}' SnapsToDevicePixels='True'>
          <ContentPresenter Margin='10,6' HorizontalAlignment='Left' VerticalAlignment='Center' />
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='bd' Property='Background' Value='#FF2F323C' />
          </Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='bd' Property='Background' Value='#FF42A5F5' />
            <Setter Property='Foreground' Value='#FF101114' />
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter Property='Opacity' Value='0.5' />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        /// <summary>
        /// Строка состояния (S25): длинные сообщения обрезаются многоточием (MaxWidth 640),
        /// полный текст живёт в подсказке.
        /// </summary>
        private void SetStatus(string text)
        {
            status.Text = text;
            status.ToolTip = text;
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
                // поправка высоты корня эталона (S32): в долях длины ноги — остаётся верной при калибровке костей
                ghost.SetGroundShift(GhostCanvas.ComputeGroundShift(track));
            }
            catch (Exception ex)
            {
                SetStatus("Эталон не загрузился (" + canonicalPath + "): " + ex.Message);
            }

            // следящий кадр (S21, T12): пиксели корня танцора по камерам, рядом с canonical.json;
            // словарь хранится отдельно — тур (S34) берёт из него рамки cur* и длительность
            camTracks = LoadCamRoot(Path.Combine(Path.GetDirectoryName(canonicalPath)!, opts.Seq + ".cams.json"));
            follower = new FrameFollower(camTracks);

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
                SetStatus("Видео " + (track?.Video ?? "?") + " не найдено в data/aist/video: призраки идут по реальным часам, без звука.");
            }

            if (!opts.NoSensor)
            {
                wantRunning = true;
                await StartAsync();
            }
            else if (videoOk)
            {
                SetStatus(mode == AppMode.Scene
                        ? "Сцена сенсора: сенсор не запущен, «Старт сенсора» его запускает."
                        : "Идёт проба: " + opts.Seq + ", без сенсора (--no-sensor). Space: пауза/плей, R: рестарт.");
            }

            // регрессионный сценарий медиа-quirks (T23): закрытие окна в конце сценария,
            // поэтому раньше --shot (одновременно их не задают)
            if (opts.Regress == "media")
            {
                await RunMediaRegression();
            }

            // тур ракурсов (S34): вместо --shot, окно закрывается в конце тура
            if (opts.Tour != null)
            {
                await RunTourAsync();
                return;
            }

            await ShotIfRequested();
        }

        private void OnTick()
        {
            tick++;
            WatchTrialSession(); // сторож сессии nuitrack-триала (T19), работает в обоих режимах окна
            UpdateFloor(); // пол сцены (T17): FloorY Kinect приоритетен, сглаживание 0.3 на тик
            if (tick % FloorRecalcTicks == 0)
            {
                RecalcFloor(); // пол по лодыжкам (S15, fallback): не чаще раза в секунду
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
        /// в fallback (база 1.6, центр кадра). Возвращает словарь камер → CamTrack (T22):
        /// разбор файла здесь, математика следящего кадра — в DanceLab.View.FrameFollower.
        /// </summary>
        private Dictionary<string, CamTrack> LoadCamRoot(string path)
        {
            var cameras = new Dictionary<string, CamTrack>();
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
                            cameras[c.Name] = new CamTrack(list.ToArray());
                        }
                    }
                }
                else
                {
                    SetStatus("cams.json без ключа cameras: авто-кадр в fallback (" + path + ").");
                }
            }
            catch (Exception ex)
            {
                SetStatus("cams.json не загрузился: авто-кадр в fallback (" + ex.Message + ").");
            }

            return cameras;
        }

        /// <summary>
        /// Один тик следящего кадра с результатом (S21, T12 → T22 → S34): авто-зум и панорама видео
        /// по bbox танцора из cams.json текущей камеры. Математика (формулы zAuto/Tx/Ty, клампы,
        /// сглаживание 0.2, окно ±0.7 с, fallback 1.6) — в DanceLab.View.FrameFollower; окно только
        /// проверяет готовность видео, кормит числами и применяет итог к трансформам. Возвращает
        /// итоговый зум, панораму и рамку танцора (тур S34 пишет их в tour.tsv); видео не готово —
        /// зум слайдера, нули и невалидная рамка.
        /// </summary>
        private (double Zoom, double PanX, double PanY, ScreenBox Box) StepVideoFrame(double t)
        {
            var sliderZoom = Math.Max(1.0, Math.Min(3.0, Math.Round(zoomSlider.Value, 1)));
            var winH = videoPanel.ActualHeight;
            if (!videoOk || !mediaOpened || media.NaturalVideoWidth <= 0 ||
                    media.ActualWidth < 10 || media.ActualHeight < 10 || winH < 10)
            {
                follower.Reset(sliderZoom);
                videoPan.X = 0;
                videoPan.Y = 0;
                ghost.SetVideoBox(false, 0, 0, 0, ghost.ActualWidth, ghost.ActualHeight, sliderZoom);
                return (sliderZoom, 0, 0, default(ScreenBox));
            }

            var frame = follower.Update(t, currentCamera,
                media.ActualWidth, winH,
                media.NaturalVideoWidth, media.NaturalVideoHeight, sliderZoom);
            videoZoom.ScaleX = frame.Zoom;
            videoZoom.ScaleY = frame.Zoom;
            videoPan.X = frame.PanX;
            videoPan.Y = frame.PanY;

            // кадр сцены (S26, S32): та же рамка танцора в пикселях панели видео, пересчитанная в размеры
            // панели сцены; линию пола сцена берёт по обуви танцора (FeetY), не по нижней кромке с запасом
            var vw = media.ActualWidth;
            var vh = winH;
            var gw = ghost.ActualWidth;
            var gh = ghost.ActualHeight;
            var kx = vw > 0 ? gw / vw : 1.0;
            var ky = vh > 0 ? gh / vh : 1.0;
            ghost.SetVideoBox(frame.Box.IsValid, frame.Box.Height * ky, frame.Box.FeetY * ky, frame.Box.CenterX * kx, gw, gh, sliderZoom);
            return frame;
        }

        /// <summary>Обёртка тика (поведение не изменилось): шаг следящего кадра без использования результата.</summary>
        private void UpdateVideoFrame(double t) => _ = StepVideoFrame(t);

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
            var userFrame = Canonicalizer.ToCanonical(pose, userBones, legM, floorYm, userHeading);

            // скоринг по часам видео, раз на новый момент времени (пауза не копит историю)
            if (scorer != null && !paused && !(Math.Abs(t - lastScoredT) < 1e-9))
            {
                lastScore = scorer.Update(t, userFrame);
                lastScoredT = t;
                coverageSum += lastScore.Coverage; // среднее покрытие за трек для финала (T20):
                coverageCount++;                   // TrialScore.Coverage — покрытие последнего кадра
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

        /// <summary>Пользователя нет: без призрака и без процента позы в HUD паузы; разворот
        /// направления взгляда сбрасывается — при новом появлении направление начинается заново.</summary>
        private void ResetUserDisplay()
        {
            ghost.SetUser(null, null);
            livePose01 = 0;
            livePoseValid = false;
            userHeading.Reset();
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

        /// <summary>Смена трекера сбрасывает калибровку ноги и оценку пола (T19): длины костей и лодыжки
        /// переживают перезапуск сессии, но конвенции суставов и координат у трекеров разные. Авто-рестарт
        /// сессии триала этот сброс НЕ вызывает.</summary>
        private void ResetBodyCalibration()
        {
            calibrated = false;
            calibStart = null;
            lastCalibUs = -1;
            userBones = null;
            userLegM = 0;
            calArmLU.Clear(); calArmLL.Clear(); calArmRU.Clear(); calArmRL.Clear();
            calLegLU.Clear(); calLegLL.Clear(); calLegRU.Clear(); calLegRL.Clear();
            lock (floorYSync)
            {
                rawFloorY = null;
            }
            floorAnkleHeights.Clear();
            ankleReady = false;
            ankleFloorYm = 0;
            lastFloorSampleUs = -1;
            floorSource = FloorSource.Estimating;
            floorYm = 0;
            if (track != null)
            {
                ghostLen = GhostLengths.FromLeg(track.LegLengthM);
                ghost.SetLengths(ghostLen);
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
            var floorLine = FloorHudLine(inv); // S15: оценка пола или «оцениваю»
            if (opts.NoSensor || session == null)
            {
                // S30: пол здесь не оценивается — строки «пол: оцениваю» нет
                hud.Text = "без сенсора" + Environment.NewLine + timeLine;
                return;
            }

            var seen = lastUserSeen.HasValue && (DateTime.UtcNow - lastUserSeen.Value).TotalSeconds <= 1.0;
            var legLine = LegHudLine(inv, seen); // T18: статус калибровки ног, только при видимом пользователе
            var score = lastScore;
            if (!seen || score == null)
            {
                hud.Text = "не вижу" + Environment.NewLine + timeLine + Environment.NewLine + floorLine
                    + (legLine == null ? "" : Environment.NewLine + legLine);
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
                if (legLine != null)
                {
                    hud.Inlines.Add(new Run(Environment.NewLine + legLine));
                }

                return;
            }

            hud.Text = "Бегущий: " + (score.Running01 * 100).ToString("0", inv) + "%   Итог: " + (score.Total01 * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Грубые: " + score.Errors.ToString(inv) + "   Покрытие: " + (score.Coverage * 100).ToString("0", inv) + "%" + Environment.NewLine
                + "Сдвиг: " + score.ShiftSec.ToString("0.00", inv) + " с   " + timeLine + Environment.NewLine
                + floorLine
                + (legLine == null ? "" : Environment.NewLine + legLine);
        }

        /// <summary>Статус калибровки длин ног в HUD (T18, аудит P1.7): сенсор жив и пользователь виден —
        /// пока !calibrated строка «нога: калибрую N с» (N = прошло из CalibrationSeconds по calibStart;
        /// перезапуск вырожденного окна сбрасывает calibStart — счёт снова с нуля), после —
        /// «нога: X.XX м, готово» (userLegM, фактически откалиброванная длина ноги).
        /// Без сенсора или без видимого пользователя строки нет.</summary>
        private string? LegHudLine(CultureInfo inv, bool seen)
        {
            if (opts.NoSensor || session == null || !seen)
            {
                return null;
            }

            if (!calibrated)
            {
                if (!calibStart.HasValue)
                {
                    return null;
                }

                var elapsed = (int)(DateTime.UtcNow - calibStart.Value).TotalSeconds;
                if (elapsed < 0)
                {
                    elapsed = 0;
                }

                if (elapsed > (int)CalibrationSeconds)
                {
                    elapsed = (int)CalibrationSeconds;
                }

                return "нога: калибрую " + elapsed.ToString(inv) + " с";
            }

            return "нога: " + userLegM.ToString("0.00", inv) + " м, готово";
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

        /// <summary>Оценка пола по лодыжкам (S15, fallback T17): 2-й перцентиль окна; пока меньше 30 значений — не готово.
        /// Считается всегда: когда FloorY Kinect исчезает, fallback подхватывается без паузы.</summary>
        private void RecalcFloor()
        {
            if (floorAnkleHeights.Count < FloorMinSamples)
            {
                ankleReady = false;
                ankleFloorYm = 0;
                return;
            }

            var sorted = new List<double>(floorAnkleHeights);
            sorted.Sort();
            var idx = (int)Math.Ceiling(sorted.Count * 0.02) - 1;
            idx = Math.Max(0, Math.Min(sorted.Count - 1, idx));
            ankleFloorYm = sorted[idx];
            ankleReady = true;
        }

        /// <summary>FloorY последнего сырого кадра сенсора (поток трекера): реконструктор строит новые
        /// SkeletonFrame и поле не переносит, поэтому пол берётся с первоисточника (T17).</summary>
        private void TrackFloorY(SkeletonFrame f)
        {
            lock (floorYSync)
            {
                rawFloorY = f.FloorY;
            }
        }

        /// <summary>Итоговый пол сцены (T17): FloorY Kinect (валидный, |y| &lt; 3 м) приоритетен над лодыжками;
        /// сглаживание 0.3 на тик для стабильности HUD и призрака, первое валидное значение принимается сразу.</summary>
        private void UpdateFloor()
        {
            double? frameFloorY;
            lock (floorYSync)
            {
                frameFloorY = rawFloorY;
            }

            var wasTracker = floorSource == FloorSource.Tracker;
            floorSource = SkeletonFrame.PickFloorSource(frameFloorY, ankleReady);
            if (floorSource == FloorSource.Tracker && frameFloorY.HasValue)
            {
                floorYm = wasTracker ? floorYm + (KinectFloorAlpha * (frameFloorY.Value - floorYm)) : frameFloorY.Value;
            }
            else
            {
                floorYm = ankleFloorYm;
            }
        }

        private string FloorHudLine(CultureInfo inv)
        {
            switch (floorSource)
            {
                case FloorSource.Tracker:
                    return "пол: " + floorYm.ToString("0.00", inv) + " м (kinect)";
                case FloorSource.Ankles:
                    return "пол: " + floorYm.ToString("0.00", inv) + " м (лодыжки)";
                default:
                    return "пол: оцениваю";
            }
        }

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
            sceneFront.SetFloor(floorYm, floorSource != FloorSource.Estimating);
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
            SetStatus(scene
                ? "Режим сцены сенсора: скелеты всех людей, вид спереди и сверху."
                : "Режим пробы: " + opts.Seq + ".");
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
                // (порог и сравнение с NaturalDuration — в MediaQuirks.IsAtEnd, T23)
                if (MediaQuirks.IsAtEnd(media.Position, media.NaturalDuration))
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

        /// <summary>Попытка (S14): пауза видео и обратный отсчёт 3-5 с поверх сцены; по нулю рестарт с нуля (скорер тоже сбрасывается).
        /// Кнопка «Ещё раз» финального экрана (T20) идёт сюда же: оверлей прячится до отсчёта.</summary>
        private void StartAttempt()
        {
            HideFinalOverlay(); // T20: новый заход — финального экрана быть не должно
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

        /// <summary>
        /// Финальный экран (T20, аудит P1.9): с оценкой — «Трек пройден», итоговый процент (Total01,
        /// крупно), грубые ошибки, среднее покрытие за трек (TrialScore.Coverage — последний кадр,
        /// поэтому среднее копится по оценённым кадрам) и длительность (ClockSeconds на момент
        /// MediaEnded). Без оценки (lastScore null: --no-sensor, сенсор не видел танцора) — карточка
        /// «Трек закончен / без оценки» с предложением повторить (S28): конец трека не должен
        /// выглядеть как пауза.
        /// </summary>
        private void ShowFinalOverlay()
        {
            var score = lastScore;
            if (score == null)
            {
                finalTitle.Text = "Трек закончен";
                finalTotal.Text = "без оценки";
                finalTotal.FontSize = 40;
                finalStats.Text = "Сенсор не видел танцора, итог не накоплен." + Environment.NewLine
                    + "«Ещё раз» или Space: попытка заново.";
            }
            else
            {
                finalTitle.Text = "Трек пройден";
                finalTotal.FontSize = 72;
                var inv = CultureInfo.InvariantCulture;
                finalTotal.Text = (score.Total01 * 100).ToString("0", inv) + "%";
                var coverage = coverageCount > 0 ? coverageSum / coverageCount : score.Coverage;
                finalStats.Text = "Грубые ошибки: " + score.Errors.ToString(inv) + Environment.NewLine
                    + "Покрытие: " + (coverage * 100).ToString("0", inv) + "%" + Environment.NewLine
                    + "Длительность: " + TimeSpan.FromSeconds(finalDurationSec).ToString(@"mm\:ss", inv);
            }

            finalCard.Visibility = Visibility.Visible;
        }

        /// <summary>Прятать финальный экран: повтор («Ещё раз», R, Space на конце) и «Закрыть»;
        /// смена ракурса оверлей сознательно не прячет (T20, решение ПМ).</summary>
        private void HideFinalOverlay() => finalCard.Visibility = Visibility.Collapsed;

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

                // позиция на кольце = ракурс (S24): раскладка из CameraRing.Layout — c05 внизу
                // (фронтальный), кнопки не перекрываются, между соседними зазор
                var (left, top) = CameraRing.Layout(i);
                var button = new Button
                {
                    Content = cam,
                    Width = CameraRing.ButtonW,
                    Height = CameraRing.ButtonH,
                    FontSize = 11,
                    Padding = new Thickness(0),
                    Tag = cam,
                };
                StyleRingButton(button, cam == currentCamera);
                Canvas.SetLeft(button, left);
                Canvas.SetTop(button, top);
                button.Click += (s, e) => SwitchAngle(cam);
                angleRing.Children.Add(button);
                ringButtons.Add(button);
            }

            ghost.CameraYawRad = YawRad(currentCamera);
        }

        /// <summary>Цвета кнопки камеры (S24): выбранная — RingActive с тёмной подписью, остальная — RingIdle со светлой.</summary>
        private static void StyleRingButton(Button button, bool selected)
        {
            button.Background = selected ? RingActive : RingIdle;
            button.Foreground = selected ? RingActiveText : Text;
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
                StyleRingButton(b, (string)b.Tag == cam);
            }

            media.Source = new Uri(newPath);
            if (pendingResume)
            {
                media.Play();
            }

            SetStatus("Ракурс " + cam + ", позиция " + pendingRestore.Value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с сохраняется...");
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
        /// Кадр (S21, S19, T12): слайдер — множитель поверх авто-зума; до обеих половин он доходит
        /// через тик — UpdateVideoFrame применяет его к видео и с тем же значением кормит сцену (S26).
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
            // видео-зум здесь не ставится: итог z = zAuto * слайдер применяется на тике
        }

        private void RestartFromZero()
        {
            HideFinalOverlay(); // T20: R, Space на конце, конец отсчёта попытки — финал прячет рестарт
            paused = false;
            lastScore = null;
            lastScoredT = double.NaN;
            coverageSum = 0;
            coverageCount = 0;
            if (track != null)
            {
                scorer = new TrialScorer(track, trialParams);
            }

            userHeading.Reset(); // разворот взгляда живой позы (S39) начинается заново

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

            trialRestarts = 0; // смена трекера — новый отсчёт сторожа триала (T19)
            trialRestartLimitHit = false;
            trialDeadSince = null;
            await StopAsync();
            ResetBodyCalibration(); // калибровка ноги и пол сбрасываются только при смене трекера (T19)
            await StartAsync();
        }

        private async Task OnSensorButton()
        {
            if (busy)
            {
                return;
            }

            // ручной Стоп/Старт сбрасывает сторож триала (T19): счётчик перезапусков, лимит, окно смерти
            trialRestarts = 0;
            trialRestartLimitHit = false;
            trialDeadSince = null;
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
                    SetStatus("Запись невозможна: сенсор не запущен.");
                    return;
                }

                var dir = Path.Combine(RepoRoot(), "data", "captures", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + s.Key + "_trial_" + opts.Seq);
                recorder = LiveRecorder.Start(s.Source, dir, "trial_" + opts.Seq, s.Version, s.Key == "nuitrack-ai" ? "CNN_HPE" : null);
                recordButton.Content = "Стоп записи";
                SetStatus("Запись: " + recorder.Path);
                return;
            }

            var frames = StopRecorder();
            SetStatus("Запись остановлена: " + frames + " кадров.");
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

        /// <summary>
        /// Сторож сессии Nuitrack-триала (T19): триальная лицензия убивает сессию каждые 3 минуты —
        /// скелетный поток замирает, призрак пользователя стоит. Только для nuitrack-трекеров
        /// (SelectedKey содержит «nuitrack»): сессия запущена (wantRunning && session != null) и
        /// SkeletonFps &lt; 0.5 дольше 5 с при Elapsed &gt; 20 с (грейс: нейросеть nuitrack-ai грузится
        /// несколько секунд без кадров — старт смертью не считать). Kinect не трогается. Рестарт —
        /// однократный цикл Stop-&gt;Start с паузой 1 с, не более 10 подряд (T19); калибровка ноги и пол
        /// не сбрасываются, видео/оценка/эталон-призрак продолжают жить.
        /// </summary>
        private void WatchTrialSession()
        {
            if (trialRestarting || trialRestartLimitHit || busy || !wantRunning || session == null ||
                    SelectedKey.IndexOf("nuitrack", StringComparison.Ordinal) < 0)
            {
                trialDeadSince = null;
                return;
            }

            var s = session;
            if (s.SkeletonFps >= TrialDeadFps || s.Elapsed.TotalSeconds <= TrialStartupGraceSec)
            {
                trialDeadSince = null; // поток жив либо ещё идёт стартовый грейс
                return;
            }

            trialDeadSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - trialDeadSince.Value).TotalSeconds < TrialDeadSeconds)
            {
                return;
            }

            trialDeadSince = null;
            trialRestarting = true;
            trialRestarts++;
            _ = RestartTrialSessionAsync(trialRestarts);
        }

        /// <summary>Авто-рестарт умершей сессии триала (T19): пауза 1 с, Stop-&gt;Start. Счётчик за запуск
        /// окна; после лимита — сенсор остановлен и статус с приглашением включить вручную (ручной
        /// Стоп/Старт сбрасывает счётчик и лимит). Калибровку ноги и пол цикл не трогает.</summary>
        private async Task RestartTrialSessionAsync(int attempt)
        {
            try
            {
                if (busy || !wantRunning)
                {
                    return;
                }

                if (attempt > TrialRestartLimit)
                {
                    trialRestartLimitHit = true;
                    wantRunning = false;
                    await StopAsync();
                    SetStatus("Nuitrack-триал: лимит перезапусков, включите сенсор вручную.");
                    return;
                }

                SetStatus("Nuitrack-триал: перезапуск сессии (" + attempt.ToString(CultureInfo.InvariantCulture) + ")");
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (busy || !wantRunning)
                {
                    return; // за паузу вмешался ручной Стоп/Старт или смена трекера — не мешаем
                }
                await StopAsync();
                await StartAsync();
                if (session != null)
                {
                    SetStatus("Nuitrack-триал: перезапуск сессии (" + attempt.ToString(CultureInfo.InvariantCulture) + ") выполнен.");
                }
            }
            catch (Exception ex)
            {
                SetStatus("Nuitrack-триал: ошибка перезапуска: " + ex.Message);
            }
            finally
            {
                trialRestarting = false;
            }
        }

        private async Task StartAsync()
        {
            busy = true;
            trackerBox.IsEnabled = false;
            sensorButton.IsEnabled = false;
            var key = SelectedKey;
            SetStatus(key == "nuitrack-ai" ? "Запускаю Nuitrack AI (загрузка нейросети, несколько секунд)..." : key == "nuitrack-native" ? "Запускаю Nuitrack нативный..." : "Запускаю Kinect SDK...");
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
                SetStatus("Сенсор не запустился: " + (error ?? "неизвестная ошибка") + " (видео и эталон-призрак продолжают идти)");
                sensorButton.Content = "Старт сенсора";
            }
            else
            {
                session = created;
                created.Source.FrameReceived += TrackFloorY; // T17: FloorY с сырых кадров сенсора
                sensorButton.Content = "Стоп сенсора";
                // S29: статус описывает текущий режим окна, а не всегда «пробу»
                SetStatus(mode == AppMode.Scene
                    ? "Сцена сенсора: трекер " + key + ". Скелеты всех людей, виды спереди и сверху."
                    : "Идёт проба: " + opts.Seq + ", трекер " + key + ".");
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
            SetStatus("Останавливаю сенсор...");
            StopRecorder();
            var old = session;
            session = null;
            if (old != null)
            {
                old.Source.FrameReceived -= TrackFloorY;
            }

            lock (floorYSync)
            {
                rawFloorY = null; // сенсор остановлен — пол снова от лодыжек или «оцениваю»
            }
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
                SetStatus("Сенсор остановлен.");
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
            var old = session;
            if (old != null)
            {
                old.Source.FrameReceived -= TrackFloorY;
            }

            try
            {
                old?.Dispose();
            }
            catch (Exception)
            {
                // закрытие окна
            }

            session = null;
            lock (floorYSync)
            {
                rawFloorY = null;
            }
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
            SavePng(full);
            Close();
        }

        /// <summary>
        /// Тур ракурсов (S34, T35, часть 2): один запуск без сенсора обходит камеры записи с файлами,
        /// на каждый момент и множитель «Кадр» ставит видео на паузу, прокручивает сглаживание кадра
        /// с шагом тика за 3 с до момента, снимает панели видео и сцены в лист «девять ракурсов»
        /// sheet-z&lt;Z&gt;-t&lt;T&gt;.png и пишет строку числового отчёта в tour.tsv; ход тура — в tour.log.
        /// Таймер окна остановлен: кадр, призрак и HUD обновляются только командами тура.
        /// TourExitCode: 0 — PASS, 1 — FAIL; окно закрывается в конце тура.
        /// </summary>
        private async Task RunTourAsync()
        {
            var inv = CultureInfo.InvariantCulture;
            var seqDir = RepoPath(Path.Combine(opts.Tour!, opts.Seq));
            var logPath = Path.Combine(seqDir, "tour.log");
            var tsvPath = Path.Combine(seqDir, "tour.tsv");
            var log = new List<string>();
            var failures = new List<string>();

            // старый tour.log удаляется до первых записей в лог: ранние WARN этого запуска
            // не должны дописаться в файл прошлого прогона и потеряться при удалении
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            // лог пишется построчно, порциями по несколько строк
            void FlushLog()
            {
                if (log.Count > 0)
                {
                    File.AppendAllLines(logPath, log);
                    log.Clear();
                }
            }

            void Say(string line)
            {
                log.Add(line);
                if (log.Count >= 4)
                {
                    FlushLog();
                }
            }

            try
            {
                if (track == null)
                {
                    // каталог записи не создаётся: своего лога у записи нет, строка провала
                    // идёт в журнал ошибок набора каталога тура
                    TourExitCode = 1;
                    File.AppendAllText(
                        RepoPath(Path.Combine(opts.Tour!, "tour-set-errors.log")),
                        opts.Seq + ": RESULT: FAIL: эталон не загрузился" + Environment.NewLine);
                    Close();
                    return;
                }

                Directory.CreateDirectory(seqDir); // каталог записи только при загрузившемся эталоне

                timer.Stop();
                media.Pause();
                paused = true;
                UpdatePlayButton();

                var canonicalPath = Path.Combine(RepoRoot(), "data", "derived", "trial", opts.Seq + ".canonical.json");
                var source = ReadCanonicalSource(canonicalPath);

                // моменты тура: из --tour-times либо пять равномерных точек от длительности эталона
                // (последний кадр track.Frames; совсем короткая запись — минимум 0.4 с)
                var duration = track.Frames.Count > 0 ? track.Frames[track.Frames.Count - 1].TSec : 0;
                if (duration < 0.4)
                {
                    duration = 0.4;
                }

                var moments = opts.TourTimes ?? TourSchedule.Times(duration, 5);
                var zooms = opts.TourZooms ?? new double[] { 1.0, 2.0, 3.0 };
                Say("seq=" + opts.Seq + " source=" + source + " moments=" + JoinNums(moments, inv) + " zooms=" + JoinNums(zooms, inv));

                // камеры с файлами видео: имя track.Video с заменённым кодом камеры (как в SwitchAngle)
                var camFiles = new List<(string Cam, string VideoPath)>();
                var videoName = track.Video;
                if (videoName != null && Regex.IsMatch(videoName, "c\\d\\d"))
                {
                    var camMatch = Regex.Match(videoName, "c\\d\\d");
                    for (var i = 1; i <= 9; i++)
                    {
                        var cam = "c" + i.ToString("00", inv);
                        var name = videoName.Remove(camMatch.Index, camMatch.Length).Insert(camMatch.Index, cam);
                        var path = FindVideo(name);
                        if (path == null)
                        {
                            Say("WARN камера " + cam + ": нет файла видео");
                            continue;
                        }

                        camFiles.Add((cam, path));
                    }
                }
                else
                {
                    for (var i = 1; i <= 9; i++)
                    {
                        Say("WARN камера c" + i.ToString("00", inv) + ": нет файла видео");
                    }
                }

                var noVideo = camFiles.Count == 0;
                if (noVideo)
                {
                    Say("видео нет ни у одной камеры: тур без видео, сцена в запасном широком виде");
                }

                // каждый запуск тура начинает отчёт заново: заголовок tour.tsv пишется всегда,
                // tour.log удалён в начале метода, чтобы дописывание строк шло с начала
                File.WriteAllText(tsvPath, string.Join("\t", TourTsvColumns) + Environment.NewLine);

                // кадры листов по ключу (момент, множитель), собираются за весь обход камер
                var shots = new Dictionary<(double T, double Z), List<(string Cam, BitmapSource? Video, BitmapSource Scene)>>();

                foreach (var (cam, videoPath) in camFiles)
                {
                    currentCamera = cam;
                    ghost.CameraYawRad = YawRad(cam);
                    mediaOpened = false;
                    media.Source = new Uri(videoPath);
                    media.Play();
                    var opened = await WaitFor(() => mediaOpened, TimeSpan.FromSeconds(15));
                    if (!opened)
                    {
                        Say("FAIL камера " + cam + ": видео не открылось за 15 с");
                        failures.Add("камера " + cam + ": видео не открылось за 15 с");
                        continue;
                    }

                    media.Pause();
                    var rows = new List<string>();
                    foreach (var t in moments)
                    {
                        await SeekTourMomentAsync(t, noVideo);
                        foreach (var z in zooms)
                        {
                            rows.Add(await TourZoomStepAsync(cam, t, z, source, noVideo, shots));
                        }

                        FlushLog();
                    }

                    File.AppendAllLines(tsvPath, rows);
                    var minOpaque = MinSceneOpaquePx(rows);
                    Say("камера " + cam + ": " + moments.Count + " моментов, " + zooms.Count + " зумов, "
                        + rows.Count + " строк, sceneOpaquePx минимум " + minOpaque.ToString(inv));
                    if (minOpaque == 0)
                    {
                        failures.Add("камера " + cam + ": кадр сцены пуст");
                    }
                }

                if (noVideo)
                {
                    // режим без видео: строки по (момент, множитель) с пустым cam и пустыми столбцами рамки
                    var rows = new List<string>();
                    foreach (var t in moments)
                    {
                        await SeekTourMomentAsync(t, noVideo);
                        foreach (var z in zooms)
                        {
                            rows.Add(await TourZoomStepAsync(string.Empty, t, z, source, noVideo, shots));
                        }

                        FlushLog();
                    }

                    File.AppendAllLines(tsvPath, rows);
                    Say("без видео: " + moments.Count + " моментов, " + zooms.Count + " зумов, " + rows.Count + " строк");
                }

                FlushLog();

                // листы после обхода всех камер: сетка 3 на 3 по камерам на каждый (момент, множитель)
                foreach (var t in moments)
                {
                    foreach (var z in zooms)
                    {
                        if (shots.TryGetValue((t, z), out var cells) && cells.Count > 0)
                        {
                            SaveTourSheet(seqDir, opts.Seq, source, t, z, cells, inv);
                        }
                    }
                }

                TourExitCode = failures.Count == 0 ? 0 : 1;
                Say("RESULT: " + (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)));
            }
            catch (Exception ex)
            {
                TourExitCode = 1;
                Say("RESULT: FAIL: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                FlushLog();
            }
            catch (Exception)
            {
                TourExitCode = 1; // лог не записался: вердикт тура недоказуем, считаем тур проваленным
            }

            Close();
        }

        /// <summary>Перемотка тура (S34) на момент: пауза на моменте и 600 мс на проявление кадра.
        /// Вызывается один раз на (камера, момент); зумы момента обходятся без повторной перемотки.</summary>
        private async Task SeekTourMomentAsync(double t, bool noVideo)
        {
            if (!noVideo)
            {
                media.Position = TimeSpan.FromSeconds(t);
                await Task.Delay(600);
            }
        }

        /// <summary>Шаг тура на (камера, момент, множитель) после перемотки момента: множитель
        /// слайдера, прогон сглаживания (свежий FrameFollower, сброс кадра сцены, 90 тиков
        /// по 1/30 с от t−3 с), HUD и компоновка, снимки панелей видео и сцены в shots, строка отчёта.</summary>
        private async Task<string> TourZoomStepAsync(string cam, double t, double z, string source, bool noVideo,
                Dictionary<(double T, double Z), List<(string Cam, BitmapSource? Video, BitmapSource Scene)>> shots)
        {
            var inv = CultureInfo.InvariantCulture;

            zoomSlider.Value = z;

            // прогон сглаживания: свежий следящий кадр, сброс кадра сцены, тики от t−3 с до t
            follower = new FrameFollower(camTracks);
            ghost.ResetFrame();
            var last = (Zoom: 1.0, PanX: 0.0, PanY: 0.0, Box: default(ScreenBox));
            for (var k = 0; k <= 89; k++)
            {
                var tt = Math.Max(0, t - 3.0 + k / 30.0);
                last = StepVideoFrame(tt);
                ghost.SetReference(track!.Sample(tt));
            }

            UpdateHud(t);
            UpdateLayout();

            // GhostCanvas не успевает отрисоваться до снимка: ожидание диспетчера не
            // гарантирует проход отрисовки окна (WPF рендерит по своему таймеру) —
            // ждём несколько кадров отрисовки (250 мс при 60 Гц)
            ghost.InvalidateVisual();
            await Task.Delay(250);

            var videoShot = CaptureElement(videoPanel);
            var sceneShot = CaptureElement(ghostPanel)!;

            // число непрозрачных пикселей кадра сцены (альфа > 0): 0 — призрак и HUD не отрисовались
            var sceneOpaquePx = CountOpaquePixels(sceneShot);
            if (!shots.TryGetValue((t, z), out var cells))
            {
                cells = new List<(string Cam, BitmapSource? Video, BitmapSource Scene)>();
                shots[(t, z)] = cells;
            }

            if (noVideo)
            {
                // одна сцена на (момент, множитель) — в листе идёт в ячейки всех девяти камер
                for (var i = 1; i <= 9; i++)
                {
                    cells.Add(("c" + i.ToString("00", inv), null, sceneShot));
                }
            }
            else
            {
                cells.Add((cam, videoShot, sceneShot));
            }

            return BuildTourRow(cam, t, z, source, last, inv, sceneOpaquePx);
        }

        /// <summary>Строка tour.tsv (S34): числа InvariantCulture «0.###», пустое поле — значения нет.
        /// Рамка cur* — bbox текущего кадра cams.json этой камеры (без расширения по времени и без
        /// запаса обуви) тем же отображением, что строит FrameFollower; ghost* — ReferenceBounds;
        /// feetDy — низ габарита призрака минус линия ног танцора, пересчитанная в панель сцены;
        /// sceneOpaquePx — число непрозрачных пикселей кадра сцены; curRootDx — корень танцора
        /// текущего кадра cams.json тем же отображением минус половина ширины окна; ghostRootDx —
        /// проекция корня эталон-призрака (ReferenceRootX) минус половина ширины панели сцены.</summary>
        private string BuildTourRow(string cam, double t, double z, string source,
                (double Zoom, double PanX, double PanY, ScreenBox Box) step, CultureInfo inv, int sceneOpaquePx)
        {
            string Num(double v) => v.ToString("0.###", inv);

            var target = follower.LastTarget;
            var winW = media.ActualWidth;
            var winH = videoPanel.ActualHeight;
            var videoW = (double)media.NaturalVideoWidth;
            var videoH = (double)media.NaturalVideoHeight;
            var hasVideo = videoOk && mediaOpened && videoW > 0 && winW > 0 && winH > 0;
            var panAtLimitX = target.MaxPanX > 0 && Math.Abs(step.PanX) >= target.MaxPanX - 0.5;
            var panAtLimitY = target.MaxPanY > 0 && Math.Abs(step.PanY) >= target.MaxPanY - 0.5;

            var boxLeft = string.Empty;
            var boxTop = string.Empty;
            var boxRight = string.Empty;
            var boxBottom = string.Empty;
            var feetY = string.Empty;
            var boxCenterDx = string.Empty;
            if (step.Box.IsValid)
            {
                boxLeft = Num(step.Box.Left);
                boxTop = Num(step.Box.Top);
                boxRight = Num(step.Box.Right);
                boxBottom = Num(step.Box.Bottom);
                feetY = Num(step.Box.FeetY);
                boxCenterDx = Num(step.Box.CenterX - (winW / 2.0));
            }

            var curLeft = string.Empty;
            var curTop = string.Empty;
            var curRight = string.Empty;
            var curBottom = string.Empty;
            var curRootDx = string.Empty;
            if (hasVideo && cam.Length > 0 && camTracks.TryGetValue(cam, out var camTrack)
                    && camTrack.TryGetBox(t, 0.0, out var cur))
            {
                var s = Math.Min(winW / videoW, winH / videoH);
                curLeft = Num((winW / 2.0) + ((cur.UMin - (videoW / 2.0)) * s * step.Zoom) + step.PanX);
                curTop = Num((winH / 2.0) + ((cur.VMin - (videoH / 2.0)) * s * step.Zoom) + step.PanY);
                curRight = Num((winW / 2.0) + ((cur.UMax - (videoW / 2.0)) * s * step.Zoom) + step.PanX);
                curBottom = Num((winH / 2.0) + ((cur.VMax - (videoH / 2.0)) * s * step.Zoom) + step.PanY);

                // корень танцора кадра ровно t: то же отображение, z — итоговый зум шага (step.Zoom)
                if (camTrack.TryGetRoot(t, out var root))
                {
                    curRootDx = Num(((root.U - (videoW / 2.0)) * s * step.Zoom) + step.PanX);
                }
            }

            var bounds = ghost.ReferenceBounds();
            var ghostLeft = string.Empty;
            var ghostTop = string.Empty;
            var ghostRight = string.Empty;
            var ghostBottom = string.Empty;
            var feetDy = string.Empty;
            if (!bounds.IsEmpty && winH > 0)
            {
                ghostLeft = Num(bounds.Left);
                ghostTop = Num(bounds.Top);
                ghostRight = Num(bounds.Right);
                ghostBottom = Num(bounds.Bottom);
                var gh = ghostPanel.ActualHeight;
                feetDy = Num(bounds.Bottom - (step.Box.FeetY * (gh / winH)));
            }

            // проекция корня эталон-призрака относительно центра панели сцены; эталона нет — NaN
            var ghostRootDx = string.Empty;
            var rootX = ghost.ReferenceRootX();
            var panelW = ghostPanel.ActualWidth;
            if (!double.IsNaN(rootX) && panelW > 0)
            {
                ghostRootDx = Num(rootX - (panelW / 2.0));
            }

            var cols = new[]
            {
                opts.Seq, source, cam, Num(t), Num(z), Num(step.Zoom), Num(target.ZAutoTarget),
                Num(step.PanX), Num(step.PanY),
                panAtLimitX ? "1" : "0", panAtLimitY ? "1" : "0",
                Num(winW), Num(winH), Num(videoW), Num(videoH),
                boxLeft, boxTop, boxRight, boxBottom, feetY, boxCenterDx,
                curLeft, curTop, curRight, curBottom,
                ghostLeft, ghostTop, ghostRight, ghostBottom, feetDy,
                sceneOpaquePx.ToString(inv),
                curRootDx, ghostRootDx,
            };
            return string.Join("\t", cols);
        }

        /// <summary>Минимум столбца sceneOpaquePx по строкам tour.tsv; нет ни одного числа — 0.
        /// Столбец ищется по имени в TourTsvColumns: он больше не последний (дальше curRootDx и
        /// ghostRootDx, S34/E54).</summary>
        private static int MinSceneOpaquePx(IEnumerable<string> rows)
        {
            var opaqueIndex = Array.IndexOf(TourTsvColumns, "sceneOpaquePx");
            var min = int.MaxValue;
            foreach (var row in rows)
            {
                var cols = row.Split('\t');
                if (cols.Length > opaqueIndex && int.TryParse(cols[opaqueIndex], out var v) && v < min)
                {
                    min = v;
                }
            }

            return min == int.MaxValue ? 0 : min;
        }

        /// <summary>Лист «девять ракурсов» sheet-z&lt;Z&gt;-t&lt;T&gt;.png (S34): фон #0A0B0E, строка-заголовок,
        /// сетка 3 на 3 по камерам, в ячейке подпись камеры и кадры видео и сцены по 300 px шириной
        /// (высота по пропорции исходника), в режиме без видео — только кадр сцены; зазоры 8 px.</summary>
        private static void SaveTourSheet(string dir, string seq, string source, double t, double z,
                List<(string Cam, BitmapSource? Video, BitmapSource Scene)> cells, CultureInfo inv)
        {
            const double FrameW = 300.0;
            const double Gap = 8.0;
            const double CaptionH = 16.0;
            const double HeaderH = 24.0;
            var columns = 3;

            double FrameH(BitmapSource bmp) => bmp.PixelWidth > 0 ? FrameW * bmp.PixelHeight / bmp.PixelWidth : FrameW * 9.0 / 16.0;

            // камеры по порядку номера слева направо и сверху вниз
            var ordered = new List<(string Cam, BitmapSource? Video, BitmapSource Scene)>(cells);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Cam, b.Cam));

            // высоты строк по самой высокой ячейке строки, верха строк, итоговый размер листа
            var rowHeights = new double[columns];
            for (var i = 0; i < ordered.Count; i++)
            {
                var frameH = Math.Max(
                    ordered[i].Video == null ? 0 : FrameH(ordered[i].Video!),
                    FrameH(ordered[i].Scene));
                rowHeights[i / columns] = Math.Max(rowHeights[i / columns], CaptionH + frameH);
            }

            var rowTops = new double[columns];
            var y = HeaderH + Gap;
            for (var r = 0; r < columns; r++)
            {
                rowTops[r] = y;
                y += rowHeights[r] + Gap;
            }

            var cellW = ordered.Count > 0 && ordered[0].Video != null ? (2 * FrameW) + Gap : FrameW;
            var width = (4 * Gap) + (columns * cellW);
            var height = y;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0A, 0x0B, 0x0E)), null, new Rect(0, 0, width, height));
                var header = new FormattedText(
                    seq + "  источник " + source + "  Кадр x" + z.ToString("0.0", inv) + "  t=" + t.ToString("0.0", inv) + " с",
                    inv, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1.0);
                dc.DrawText(header, new Point(Gap, 4));

                var caption = new Typeface("Segoe UI");
                for (var i = 0; i < ordered.Count; i++)
                {
                    var x0 = Gap + ((i % columns) * (cellW + Gap));
                    var y0 = rowTops[i / columns];
                    dc.DrawText(new FormattedText(ordered[i].Cam, inv, FlowDirection.LeftToRight, caption, 12, Brushes.White, 1.0),
                        new Point(x0, y0));

                    var frameY = y0 + CaptionH;
                    if (ordered[i].Video != null)
                    {
                        dc.DrawImage(ordered[i].Video, new Rect(x0, frameY, FrameW, FrameH(ordered[i].Video!)));
                        dc.DrawImage(ordered[i].Scene, new Rect(x0 + FrameW + Gap, frameY, FrameW, FrameH(ordered[i].Scene)));
                    }
                    else
                    {
                        dc.DrawImage(ordered[i].Scene, new Rect(x0, frameY, FrameW, FrameH(ordered[i].Scene)));
                    }
                }
            }

            var bmp = new RenderTargetBitmap((int)Math.Round(width), (int)Math.Round(height), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            var path = Path.Combine(dir, "sheet-z" + z.ToString("0.0", inv) + "-t" + t.ToString("0.0", inv) + ".png");
            using (var fs = File.Create(path))
            {
                enc.Save(fs);
            }
        }

        /// <summary>Снимок элемента в PNG-совместимый BitmapSource (панели тура); пустой элемент — null.
        /// Рендер через VisualBrush: RenderTargetBitmap.Render(элемент-потомок) рисует содержимое со
        /// сдвигом VisualOffset элемента внутри окна — панель сцены (вторая колонка сетки) уезжала
        /// целиком за границы снимка, кадр выходил пустым; кисть же рисует содержимое от (0, 0).</summary>
        private static BitmapSource? CaptureElement(FrameworkElement element)
        {
            var w = (int)Math.Round(element.ActualWidth);
            var h = (int)Math.Round(element.ActualHeight);
            if (w <= 0 || h <= 0)
            {
                return null;
            }

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = h,
                Fill = new VisualBrush(element),
            };
            rect.Measure(new Size(w, h));
            rect.Arrange(new Rect(0, 0, w, h));

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(rect);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Число непрозрачных пикселей кадра (байт альфы, каждый четвёртый, больше 0); null — 0.</summary>
        private static int CountOpaquePixels(BitmapSource? bmp)
        {
            if (bmp == null)
            {
                return 0;
            }

            var pixels = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
            bmp.CopyPixels(pixels, bmp.PixelWidth * 4, 0);
            var count = 0;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Поле source из canonical.json записи (как LoadCamRoot — JsonDocument); нет поля — пусто.</summary>
        private static string ReadCanonicalSource(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                return doc.RootElement.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString() ?? string.Empty
                        : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Список чисел через запятую для строки tour.log (InvariantCulture, одна десятая минимум).</summary>
        private static string JoinNums(IEnumerable<double> values, CultureInfo inv)
        {
            var parts = new List<string>();
            foreach (var v in values)
            {
                parts.Add(v.ToString("0.0#", inv));
            }

            return string.Join(",", parts);
        }

        /// <summary>Рендер содержимого окна в PNG (используется --shot и --regress media).</summary>
        private void SavePng(string full)
        {
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
        }

        /// <summary>
        /// Регрессионный сценарий медиа-quirks MediaElement (T23, аудит P2.13, --regress media), один запуск
        /// окна: старт (R с нуля, чтобы конец трека не наступил посреди сценария) → пауза Space на 1.5 с →
        /// плей → скорость 2.0 слайдером на 1 с (два замера позиции против замера при 1.0) → обратно 1.0 →
        /// R-рестарт → конец трека (MediaEnded) → Space на конце (quirk: Play с конца — должен рестартовать
        /// с нуля, ветка TogglePlay) → 1 с → Space (пауза) → скриншот data/preview/trial-regress-media.png.
        /// Каждый шаг пишет в лог data/preview/trial-regress-media.log позицию и paused; утверждения —
        /// позиция &lt; 1 с после каждого рестарта и после Space на конце, скорость x2 по двум замерам —
        /// дают PASS/FAIL в лог и статус. Клавиши идут по реальному пути обработки окна
        /// (RaiseEvent PreviewKeyDown), слайдер — программно. Выход: exit 0, вердикт — в логе и статусе.
        /// </summary>
        private async Task RunMediaRegression()
        {
            var inv = CultureInfo.InvariantCulture;
            var log = new List<string>();
            var failures = new List<string>();
            void Say(string line) => log.Add("[" + DateTime.Now.ToString("HH:mm:ss.fff", inv) + "] " + line);
            void Assert(bool ok, string name, string detail)
            {
                Say("ASSERT " + name + ": " + (ok ? "PASS" : "FAIL") + " (" + detail + ")");
                if (!ok)
                {
                    failures.Add(name + " (" + detail + ")");
                }
            }

            Say("regress media: старт (seq=" + opts.Seq + ", no-sensor=" + opts.NoSensor + ")");
            try
            {
                // старт: открыть видео и рестарт с нуля — известное исходное состояние (позиция 0, идёт)
                var opened = await WaitFor(() => mediaOpened, TimeSpan.FromSeconds(15));
                Assert(opened, "видео открылось", opened ? "mediaOpened=True, duration=" + Dur() : "таймаут 15 с, mediaOpened=False");
                SendKey(Key.R);
                await Task.Delay(300);
                Say("шаг=старт(R) pos=" + Pos() + "s paused=" + paused + " duration=" + Dur());
                Assert(media.Position.TotalSeconds < 1.0, "после старт-R позиция<1с", "pos=" + Pos() + "s");

                // пауза Space на 1.5 с
                SendKey(Key.Space);
                await Task.Delay(200);
                var pausedBySpace = paused;
                await Task.Delay(1500);
                Say("шаг=пауза(Space) pos=" + Pos() + "s paused=" + paused);
                Assert(pausedBySpace, "Space ставит паузу", "paused=" + pausedBySpace);
                Assert(media.Position.TotalSeconds >= 0.0 && paused, "позиция на паузе зафиксирована", "pos=" + Pos() + "s paused=" + paused);

                // плей
                SendKey(Key.Space);
                await Task.Delay(300);
                Say("шаг=плей(Space) pos=" + Pos() + "s paused=" + paused);
                Assert(!paused, "Space снимает паузу", "paused=" + paused);

                // замер 1: прирост позиции за 1 с при скорости 1.0
                var a0 = media.Position.TotalSeconds;
                await Task.Delay(1000);
                var delta1 = media.Position.TotalSeconds - a0;
                Say("замер1 скорость=1.0 delta=" + delta1.ToString("0.000", inv) + "s");

                // скорость 2.0 (слайдер программно), замер 2 за ту же стеночную 1 с
                speedSlider.Value = 2.0;
                await Task.Delay(200);
                Say("шаг=скорость2.0 SpeedRatio=" + media.SpeedRatio.ToString("0.0", inv));
                var b0 = media.Position.TotalSeconds;
                await Task.Delay(1000);
                var delta2 = media.Position.TotalSeconds - b0;
                Assert(media.SpeedRatio == 2.0, "SpeedRatio=2.0 на лету", "SpeedRatio=" + media.SpeedRatio.ToString("0.000", inv));
                var ratio = delta1 > 0.05 ? delta2 / delta1 : double.NaN;
                Assert(!double.IsNaN(ratio) && ratio >= 1.5 && ratio <= 2.5,
                    "скорость x2 по двум замерам",
                    "delta1=" + delta1.ToString("0.000", inv) + "s delta2=" + delta2.ToString("0.000", inv) + "s ratio=" + ratio.ToString("0.00", inv));
                Say("шаг=скорость2.0-замер2 delta=" + delta2.ToString("0.000", inv) + "s ratio=" + ratio.ToString("0.00", inv));

                // обратно 1.0
                speedSlider.Value = 1.0;
                await Task.Delay(200);
                Say("шаг=скорость1.0 SpeedRatio=" + media.SpeedRatio.ToString("0.0", inv));
                Assert(media.SpeedRatio == 1.0, "скорость вернулась к 1.0", "SpeedRatio=" + media.SpeedRatio.ToString("0.000", inv));

                // R-рестарт
                SendKey(Key.R);
                await Task.Delay(300);
                Say("шаг=рестарт(R) pos=" + Pos() + "s paused=" + paused);
                Assert(media.Position.TotalSeconds < 1.0, "после R-рестарта позиция<1с", "pos=" + Pos() + "s");

                // конец трека: MediaEnded ставит paused у конца
                var ended = await WaitFor(() => paused && MediaQuirks.IsAtEnd(media.Position, media.NaturalDuration), TimeSpan.FromSeconds(20));
                Say("шаг=конецТрека pos=" + Pos() + "s paused=" + paused + " duration=" + Dur());
                Assert(ended, "MediaEnded наступил", ended ? "paused=True у конца" : "таймаут 20 с, paused=" + paused + " pos=" + Pos() + "s");

                // Space на конце: quirk Play-с-конца — ветка TogglePlay должна рестартовать с нуля
                SendKey(Key.Space);
                await Task.Delay(300);
                Say("шаг=Space-на-конце pos=" + Pos() + "s paused=" + paused);
                Assert(media.Position.TotalSeconds < 1.0, "после MediaEnded+Space позиция<1с", "pos=" + Pos() + "s");

                await Task.Delay(1000);
                SendKey(Key.Space);
                await Task.Delay(200);
                Say("шаг=пауза-после-рестарта(Space) pos=" + Pos() + "s paused=" + paused);
                Assert(paused, "финальный Space ставит паузу", "paused=" + paused);
            }
            catch (Exception ex)
            {
                Say("ASSERT сценарий упал: FAIL (" + ex.GetType().Name + ": " + ex.Message + ")");
                failures.Add("исключение: " + ex.GetType().Name + ": " + ex.Message);
            }

            Say("RESULT: " + (failures.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", failures)));
            SetStatus(failures.Count == 0
                ? "Регресс media: PASS."
                : "Регресс media: FAIL — " + failures[0]);
            File.AppendAllLines(RepoPath("data/preview/trial-regress-media.log"), log);
            SavePng(RepoPath("data/preview/trial-regress-media.png"));
            Close();
        }

        /// <summary>Ожидание условия с таймаутом (опрос раз в 100 мс); возвращает итоговую проверку.</summary>
        private static async Task<bool> WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return condition();
        }

        /// <summary>Клавиша окну по реальному пути обработки: RaiseEvent PreviewKeyDown (обработчик Space/R).</summary>
        private void SendKey(Key key) =>
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });

        /// <summary>Позиция видео в секундах для лога.</summary>
        private string Pos() => media.Position.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

        /// <summary>Длительность трека (NaturalDuration) для лога; без TimeSpan — «нет».</summary>
        private string Dur() => media.NaturalDuration.HasTimeSpan
            ? media.NaturalDuration.TimeSpan.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)
            : "нет";

        /// <summary>Путь внутри репозитория с созданием каталога (лог и скриншот регресса).</summary>
        private static string RepoPath(string rel)
        {
            var full = Path.IsPathRooted(rel) ? rel : Path.Combine(RepoRoot(), rel);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return full;
        }
    }
}
