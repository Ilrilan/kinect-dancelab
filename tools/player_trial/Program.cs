using System;
using System.Text.RegularExpressions;
using System.Windows;

namespace PlayerTrial
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var app = new Application();
            return app.Run(new MainWindow(Options.Parse(Environment.GetCommandLineArgs())));
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

        public double ShotAfterSec { get; private set; } = 8;

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

            return o;
        }
    }
}
