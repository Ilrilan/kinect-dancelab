using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DanceLab.Capture
{
    public enum CaptureExitCode
    {
        Ok = 0,
        PreflightFailed = 2,
        NoSensor = 3,
        BufferOverflow = 4,
    }

    public sealed class CaptureOptions
    {
        public string Scenario { get; set; } = "unnamed";

        /// <summary>Папка прогона: в неё пишутся skeleton.jsonl и, в режиме глубины и цвета, остальные файлы.</summary>
        public string RunDirectory { get; set; } = string.Empty;

        public double DurationSeconds { get; set; } = 10;

        /// <summary>Режим глубины и цвета (S5): пишутся кадры глубины и цвета помимо скелета.</summary>
        public bool Images { get; set; }

        public int CountdownSeconds { get; set; } = 3;

        public int WarmupMilliseconds { get; set; } = 2000;

        public int AvailabilityWaitMilliseconds { get; set; } = 10000;

        public string TrackerVersion { get; set; } = "unknown";

        /// <summary>Версия трекера, если она известна только после запуска источника (Nuitrack).</summary>
        public Func<string>? VersionProvider { get; set; }

        /// <summary>Возвращает режим трекера (например Skeletonization.Type) после запуска источника.</summary>
        public Func<string?>? EngineProvider { get; set; }

        public Func<long>? FreeMemoryProvider { get; set; }

        public Action<string>? Log { get; set; }
    }

    public sealed class CaptureResult
    {
        public CaptureResult(CaptureExitCode code, string message, int skeletonFrames, int depthFrames, int colorFrames)
        {
            Code = code;
            Message = message;
            SkeletonFrames = skeletonFrames;
            DepthFrames = depthFrames;
            ColorFrames = colorFrames;
        }

        public CaptureExitCode Code { get; }
        public string Message { get; }
        public int SkeletonFrames { get; }
        public int DepthFrames { get; }
        public int ColorFrames { get; }
    }

    /// <summary>Один прогон замеров: старт источника, предпроверка памяти, запись заданное время, сохранение.</summary>
    public static class CaptureSession
    {
        public static CaptureResult Run(IPoseSource pose, IRawImageSource? images, CaptureOptions o)
        {
            void Log(string text) => o.Log?.Invoke(text);

            if (o.Images && images == null)
            {
                return new CaptureResult(CaptureExitCode.NoSensor, "Источник не умеет отдавать глубину и цвет.", 0, 0, 0);
            }

            try
            {
                try
                {
                    pose.Start();
                    if (o.Images)
                    {
                        images!.StartImages();
                    }
                }
                catch (Exception ex)
                {
                    return new CaptureResult(CaptureExitCode.NoSensor, "Не удалось открыть сенсор: " + ex.Message, 0, 0, 0);
                }

                Thread.Sleep(o.WarmupMilliseconds);
                var waited = 0;
                while (!pose.IsAvailable && waited < o.AvailabilityWaitMilliseconds)
                {
                    Thread.Sleep(250);
                    waited += 250;
                }

                if (!pose.IsAvailable)
                {
                    return new CaptureResult(CaptureExitCode.NoSensor, "Сенсор недоступен: трекер не отдаёт данные (проверьте питание и USB, улучшение звука у Xbox NUI Sensor должно быть выключено).", 0, 0, 0);
                }

                var sensor = pose.Sensor;
                var header = new RunHeader(pose.TrackerId, o.VersionProvider?.Invoke() ?? o.TrackerVersion, o.EngineProvider?.Invoke(), sensor, DateTimeOffset.UtcNow, o.Scenario);

                var depthCap = 0;
                var colorCap = 0;
                if (o.Images)
                {
                    if (sensor.Depth == null || sensor.Color == null)
                    {
                        return new CaptureResult(CaptureExitCode.NoSensor, "Трекер не сообщил размеры кадров глубины и цвета.", 0, 0, 0);
                    }

                    depthCap = (int)Math.Ceiling(o.DurationSeconds * Math.Max(1, sensor.Depth.Fps)) + 15;
                    colorCap = (int)Math.Ceiling(o.DurationSeconds * Math.Max(1, sensor.Color.Fps)) + 15;
                    var need = MemoryPreflight.EstimateBytes(depthCap, MemoryPreflight.FrameBytes(sensor.Depth), colorCap, MemoryPreflight.FrameBytes(sensor.Color));
                    var free = (o.FreeMemoryProvider ?? MemoryPreflight.GetFreePhysicalBytes)();
                    var pre = MemoryPreflight.Check(need, free);
                    Log(string.Format(CultureInfo.InvariantCulture, "Память: нужно {0:N0} МБ, свободно {1:N0} МБ, запас x{2}.", need / 1048576, free / 1048576, MemoryPreflight.DefaultFactor));
                    if (!pre.Ok)
                    {
                        return new CaptureResult(
                            CaptureExitCode.PreflightFailed,
                            string.Format(CultureInfo.InvariantCulture, "Недостаточно свободной памяти: освободите ещё {0:N0} МБ и повторите.", (pre.ToFreeBytes / 1048576) + 1),
                            0,
                            0,
                            0);
                    }
                }

                Directory.CreateDirectory(o.RunDirectory);
                for (var c = o.CountdownSeconds; c > 0; c--)
                {
                    Log("Старт через " + c + "...");
                    Thread.Sleep(1000);
                }

                var skeletonPath = Path.Combine(o.RunDirectory, "skeleton.jsonl");
                var writer = new SkeletonRecordWriter(new FileStream(skeletonPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16), header, 8192);
                var recorder = o.Images ? new ImageRunRecorder(depthCap, colorCap) : null;
                pose.FrameReceived += writer.Write;
                if (recorder != null)
                {
                    images!.DepthReceived += recorder.AddDepth;
                    images.ColorReceived += recorder.AddColor;
                }

                Log("Запись " + o.DurationSeconds.ToString(CultureInfo.InvariantCulture) + " с...");
                var end = DateTime.UtcNow.AddSeconds(o.DurationSeconds);
                while (DateTime.UtcNow < end)
                {
                    Thread.Sleep(50);
                }

                pose.FrameReceived -= writer.Write;
                if (recorder != null)
                {
                    images!.DepthReceived -= recorder.AddDepth;
                    images.ColorReceived -= recorder.AddColor;
                }

                var code = CaptureExitCode.Ok;
                var message = "Готово.";
                try
                {
                    writer.Complete();
                }
                catch (CaptureBufferOverflowException ex)
                {
                    code = CaptureExitCode.BufferOverflow;
                    message = ex.Message;
                }
                finally
                {
                    writer.Dispose();
                }

                if (recorder != null)
                {
                    recorder.Save(o.RunDirectory, header);
                }

                return new CaptureResult(code, message, writer.FramesWritten, recorder?.DepthCount ?? 0, recorder?.ColorCount ?? 0);
            }
            finally
            {
                try
                {
                    pose.Stop();
                    if (o.Images)
                    {
                        images!.StopImages();
                    }
                }
                catch (Exception ex)
                {
                    Log("Остановка сенсора: " + ex.Message);
                }
            }
        }
    }
}
