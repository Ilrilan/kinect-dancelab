using System;
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

            o.NoSensor = Array.IndexOf(args, "--no-sensor") >= 0;
            o.ShotPath = Opt("--shot");
            if (double.TryParse(Opt("--shot-after"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var after) && after > 0)
            {
                o.ShotAfterSec = after;
            }

            return o;
        }
    }
}
