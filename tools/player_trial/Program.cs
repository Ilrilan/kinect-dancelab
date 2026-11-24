using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace PlayerTrial
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var opts = Options.Parse(Environment.GetCommandLineArgs());
            var app = new Application();

            // тур по списку записей (--tour-set, T42): окна записей по очереди в одном процессе
            if (opts.TourSet != null)
            {
                return RunTourSet(app, opts);
            }

            // тур (S34): код возврата процесса — вердикт тура (0 PASS, 1 FAIL), если он был
            var window = new MainWindow(opts);
            var code = app.Run(window);
            return MainWindow.TourExitCode != 0 ? MainWindow.TourExitCode : code;
        }

        /// <summary>
        /// Тур по списку записей (--tour-set, T42): на каждую запись своё окно в одном процессе.
        /// Повторный app.Run(window) в WPF невозможен, поэтому схема: одно app.Run() без окна,
        /// окна показываются по очереди Show(), следующее открывается в обработчике Closed
        /// предыдущего, app.Shutdown() после последнего. Итог тура — в data/preview/tour/tour-set.log,
        /// код возврата 0, если все записи прошли (TourExitCode у каждой записи).
        /// </summary>
        private static int RunTourSet(Application app, Options opts)
        {
            var root = RepoRoot();
            var setPath = Path.IsPathRooted(opts.TourSet!) ? opts.TourSet! : Path.Combine(root, opts.TourSet!);
            var logPath = Path.Combine(root, "data", "preview", "tour", "tour-set.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            // журнал набора начинается заново на каждый запуск
            File.WriteAllText(logPath, string.Empty);

            if (!File.Exists(setPath))
            {
                File.WriteAllText(logPath, "RESULT: FAIL: нет файла списка " + opts.TourSet + Environment.NewLine);
                return 1;
            }

            // список записей: первый токен строки, пустые строки и комментарии с # пропускаются
            var seqs = new List<string>();
            foreach (var line in File.ReadAllLines(setPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                seqs.Add(trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0]);
            }

            if (seqs.Count == 0)
            {
                File.AppendAllText(logPath, "RESULT: PASS" + Environment.NewLine);
                return 0;
            }

            var exit = 0;
            var next = 0;
            void RunNext()
            {
                if (next >= seqs.Count)
                {
                    File.AppendAllText(logPath, "RESULT: " + (exit == 0 ? "PASS" : "FAIL") + Environment.NewLine);
                    app.Shutdown();
                    return;
                }

                var seq = seqs[next++];
                var window = new MainWindow(opts, seq);
                window.Closed += (s, e) =>
                {
                    if (MainWindow.TourExitCode != 0)
                    {
                        exit = 1;
                        File.AppendAllText(logPath, seq + ": FAIL" + Environment.NewLine);
                    }

                    RunNext();
                };
                window.Show();
            }

            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunNext();
            app.Run();
            return exit;
        }

        /// <summary>Корень репозитория: ближайший предок каталога бинарника с DanceLab.sln (как MainWindow.RepoRoot).</summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DanceLab.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    /// <summary>Параметры командной строки пробного плеера (specs/player/trial.md Contract).</summary>
    internal sealed class Options
    {
        public const string DefaultSeq = "gLO_sBM_cAll_d14_mLO5_ch02";

        public string Seq { get; private set; } = DefaultSeq;

        /// <summary>kinect, nuitrack, nuitrack-native или nuitrack-ai (nuitrack то же, что nuitrack-ai).</summary>
        public string Tracker { get; private set; } = "kinect";

        /// <summary>Стартовый режим окна (S16): probe (проба, по умолчанию) или scene (сцена сенсора).</summary>
        public string Mode { get; private set; } = "probe";

        /// <summary>Стартовый ракурс (S13): c01..c09, null — ракурс видео эталона (c05).</summary>
        public string? Camera { get; private set; }

        /// <summary>Стартовый зум кадра (S21): 1.0..3.0, null — умолчание слайдера 1.6.</summary>
        public double? Zoom { get; private set; }

        public bool NoSensor { get; private set; }

        public string? ShotPath { get; private set; }

        /// <summary>Регрессионный сценарий за один запуск окна (T23, аудит P2.13): сейчас только media.</summary>
        public string? Regress { get; private set; }

        /// <summary>Тур (S34): каталог листов и отчётов из --tour; относительный путь — от корня репозитория.</summary>
        public string? Tour { get; private set; }

        /// <summary>Тур по списку записей (T42): файл списка из --tour-set, одна запись на окно того же процесса.</summary>
        public string? TourSet { get; private set; }

        /// <summary>Моменты тура в секундах (S34) из --tour-times: только положительные конечные.</summary>
        public IReadOnlyList<double>? TourTimes { get; private set; }

        /// <summary>Множители «Кадр» тура (S34) из --tour-zooms: зажаты в 1..3, порядок и повторы сохранены.</summary>
        public IReadOnlyList<double>? TourZooms { get; private set; }

        public double ShotAfterSec { get; private set; } = 8;

        /// <summary>Копия Options с новой записью для очередного окна тура --tour-set (T42).</summary>
        public Options WithSeq(string seq) => new Options
        {
            Seq = seq,
            Tracker = Tracker,
            Mode = Mode,
            Camera = Camera,
            Zoom = Zoom,
            NoSensor = NoSensor,
            ShotPath = ShotPath,
            Regress = Regress,
            Tour = Tour,
            TourTimes = TourTimes,
            TourZooms = TourZooms,
            ShotAfterSec = ShotAfterSec,
            TourSet = TourSet,
        };

        public static Options Parse(string[] args)
        {
            var o = new Options();
            string? Opt(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            o.Seq = Opt("--seq") ?? DefaultSeq;
            var tracker = Opt("--tracker");
            if (tracker == "nuitrack" || tracker == "nuitrack-native" || tracker == "nuitrack-ai")
            {
                o.Tracker = tracker;
            }

            var mode = Opt("--mode");
            if (mode == "probe" || mode == "scene")
            {
                o.Mode = mode;
            }

            // --camera cXX (или просто номер): стартовый ракурс видео и 3D-сцены
            var camera = Opt("--camera");
            var cm = string.IsNullOrEmpty(camera) ? null : Regex.Match(camera, "c?(\\d{1,2})");
            if (cm != null && cm.Success && int.TryParse(cm.Groups[1].Value, out var camN) && camN >= 1 && camN <= 9)
            {
                o.Camera = "c" + camN.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            }

            o.NoSensor = Array.IndexOf(args, "--no-sensor") >= 0;

            // --zoom число (1.0..3.0): стартовый зум кадра (S21) без слайдера; вне диапазона — игнор
            if (double.TryParse(Opt("--zoom"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var zoom) && zoom >= 1.0 && zoom <= 3.0)
            {
                o.Zoom = zoom;
            }

            o.ShotPath = Opt("--shot");
            if (double.TryParse(Opt("--shot-after"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var after) && after > 0)
            {
                o.ShotAfterSec = after;
            }

            // --regress media (T23): сценарий медиа-quirks за один запуск окна (лог + скриншот в data/preview)
            var regress = Opt("--regress");
            if (regress == "media")
            {
                o.Regress = regress;
            }

            // --tour <каталог> (S34): тур вместо --shot, каталог листов и отчётов; путь сохраняется
            // как есть (относительный считается от корня репозитория), сенсор в туре не стартует
            o.Tour = Opt("--tour");

            // --tour-set <файл> (T42): тур по списку записей; без --tour каталог тура по умолчанию
            o.TourSet = Opt("--tour-set");
            if (o.TourSet != null && o.Tour == null)
            {
                o.Tour = "data/preview/tour";
            }

            if (o.Tour != null)
            {
                o.NoSensor = true;
            }

            // --tour-times <с,с,...> (S34): моменты в секундах, только положительные конечные
            var tourTimes = ParseDoubleList(Opt("--tour-times"));
            tourTimes.RemoveAll(v => v <= 0);
            if (tourTimes.Count > 0)
            {
                o.TourTimes = tourTimes;
            }

            // --tour-zooms <мн,мн,...> (S34): множители «Кадр», зажаты в отрезок 1..3
            var tourZooms = ParseDoubleList(Opt("--tour-zooms"));
            for (var i = 0; i < tourZooms.Count; i++)
            {
                tourZooms[i] = Math.Min(3.0, Math.Max(1.0, tourZooms[i]));
            }

            if (tourZooms.Count > 0)
            {
                o.TourZooms = tourZooms;
            }

            return o;
        }

        /// <summary>
        /// Список чисел через запятую для тура (S34) в InvariantCulture: непарсящиеся и не конечные
        /// (NaN, бесконечность) значения отбрасываются; порядок и повторы сохраняются.
        /// </summary>
        private static List<double> ParseDoubleList(string? raw)
        {
            var values = new List<double>();
            if (raw == null)
            {
                return values;
            }

            foreach (var token in raw.Split(','))
            {
                if (double.TryParse(token.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                    && !double.IsNaN(v) && !double.IsInfinity(v))
                {
                    values.Add(v);
                }
            }

            return values;
        }
    }
}
