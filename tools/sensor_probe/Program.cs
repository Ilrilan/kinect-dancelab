using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DanceLab.Capture;

namespace SensorProbe
{
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitPreflight = 2;
        private const int ExitNoSensor = 3;
        private const int ExitOverflow = 4;

        private sealed class Options
        {
            public string Tracker = string.Empty;
            public string Scenario = "unnamed";
            public string Mode = "skeleton";
            public double Duration = 10;
            public string SensorId = "sensor-1";
            public string Out = Path.Combine("data", "captures");
            public string NuitrackEngine = "CNN_HPE";
            public int Countdown = 3;
            public bool NoColorSensor;
            public System.Collections.Generic.List<string> NuitrackSet = new System.Collections.Generic.List<string>();
        }

        private static int Usage(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine("sensor_probe --tracker kinect|nuitrack --scenario <имя> --mode skeleton|depthcolor");
            Console.Error.WriteLine("             [--duration <секунд, 10>] [--sensor-id <имя, sensor-1>] [--out <папка, data/captures>]");
            Console.Error.WriteLine("             [--nuitrack-engine <CNN_HPE|config, CNN_HPE>] [--countdown <секунд, 3>]");
            Console.Error.WriteLine("Коды выхода: 0 успех, 1 ошибка аргументов, 2 отказ предпроверки памяти, 3 нет сенсора, 4 переполнение буфера.");
            return ExitUsage;
        }

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var o = new Options();
            try
            {
                for (var i = 0; i < args.Length; i++)
                {
                    string Next()
                    {
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException("Нет значения для " + args[i]);
                        }

                        return args[++i];
                    }

                    switch (args[i])
                    {
                        case "--tracker": o.Tracker = Next(); break;
                        case "--scenario": o.Scenario = Next(); break;
                        case "--mode": o.Mode = Next(); break;
                        case "--duration": o.Duration = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                        case "--sensor-id": o.SensorId = Next(); break;
                        case "--out": o.Out = Next(); break;
                        case "--nuitrack-engine": o.NuitrackEngine = Next(); break;
                        case "--nuitrack-set": o.NuitrackSet.Add(Next()); break;
                        case "--no-color-sensor": o.NoColorSensor = true; break;
                        case "--countdown": o.Countdown = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                        default: return Usage("Неизвестный аргумент: " + args[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                return Usage(ex.Message);
            }

            if (o.Tracker != "kinect" && o.Tracker != "nuitrack")
            {
                return Usage("--tracker должен быть kinect или nuitrack.");
            }

            if (o.Mode != "skeleton" && o.Mode != "depthcolor")
            {
                return Usage("--mode должен быть skeleton или depthcolor.");
            }

            if (o.Duration <= 0 || o.Duration > 120)
            {
                return Usage("--duration должен быть от 0 до 120 секунд.");
            }

            return Run(o);
        }

        private sealed class Handle
        {
            public IPoseSource Pose = null!;
            public IRawImageSource Images = null!;
            public Func<string> Version = () => "unknown";
            public Func<string?> LastError = () => null;
            public Func<string?>? Engine;
            public Func<string?> Extra = () => null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Handle CreateKinect(Options o)
        {
            var s = new DanceLab.Capture.Kinect.KinectPoseSource(o.SensorId);
            return new Handle { Pose = s, Images = s, Version = () => s.Version };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Handle CreateNuitrack(Options o)
        {
            var engine = o.NuitrackEngine == "config" ? null : o.NuitrackEngine;
            var s = new DanceLab.Capture.Nuitrack.NuitrackPoseSource(o.SensorId, engine) { UseColorSensor = !o.NoColorSensor };
            foreach (var kv in o.NuitrackSet)
            {
                var i = kv.IndexOf((char)61);
                s.ConfigOverrides[i < 0 ? kv : kv.Substring(0, i)] = i < 0 ? "?" : kv.Substring(i + 1);
            }
            return new Handle { Pose = s, Images = s, Version = () => s.Version, LastError = () => s.LastError, Engine = () => s.Engine, Extra = () => string.Join("; ", System.Linq.Enumerable.Select(s.ConfigResults, r => r.Key + "=" + r.Value)) };
        }

        private static int Run(Options o)
        {
            Handle h;
            try
            {
                h = o.Tracker == "kinect" ? CreateKinect(o) : CreateNuitrack(o);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Нет сенсора или трекера: " + ex.GetType().Name + ": " + ex.Message);
                return ExitNoSensor;
            }

            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var options = new CaptureOptions
                {
                    Scenario = o.Scenario,
                    RunDirectory = Path.Combine(o.Out, stamp + "_" + o.Tracker + "_" + o.Scenario),
                    DurationSeconds = o.Duration,
                    Images = o.Mode == "depthcolor",
                    CountdownSeconds = o.Countdown,
                    VersionProvider = h.Version,
                    EngineProvider = h.Engine,
                    Log = Console.WriteLine,
                };
                Console.WriteLine("Открываю сенсор через " + o.Tracker + "...");
                var result = CaptureSession.Run(h.Pose, h.Images, options);
                Console.WriteLine(result.Message);
                Console.WriteLine("Кадров скелета: " + result.SkeletonFrames + ", глубины: " + result.DepthFrames + ", цвета: " + result.ColorFrames);
                Console.WriteLine("Папка: " + options.RunDirectory);
                var extra = h.Extra();
                if (!string.IsNullOrEmpty(extra))
                {
                    Console.WriteLine("Конфиг Nuitrack: " + extra);
                }

                var err = h.LastError();
                if (err != null)
                {
                    Console.Error.WriteLine("Предупреждение трекера: " + err);
                }

                switch (result.Code)
                {
                    case CaptureExitCode.Ok: return ExitOk;
                    case CaptureExitCode.PreflightFailed: return ExitPreflight;
                    case CaptureExitCode.NoSensor: return ExitNoSensor;
                    default: return ExitOverflow;
                }
            }
            finally
            {
                try
                {
                    h.Pose.Dispose();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Закрытие сенсора: " + ex.Message);
                }
            }
        }
    }
}
