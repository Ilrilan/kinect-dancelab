using System;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace DanceLab.Capture.Tests
{
    public class CaptureSessionTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "capture-session-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private static SensorInfo Sensor() => new SensorInfo("s1", "SN", new FrameSize(8, 4, 30, "Depth16"), new FrameSize(8, 4, 30, "Bgra"));

        private static CaptureOptions Options(string dir, bool images, Func<long>? free = null) => new CaptureOptions
        {
            Scenario = "test",
            RunDirectory = dir,
            DurationSeconds = 1,
            Images = images,
            CountdownSeconds = 0,
            WarmupMilliseconds = 10,
            AvailabilityWaitMilliseconds = 500,
            TrackerVersion = "t1",
            FreeMemoryProvider = free ?? (() => 8L * 1024 * 1024 * 1024),
        };

        private static Thread Pump(FakePoseSource src, bool images, CancellationToken stop)
        {
            var t = new Thread(() =>
            {
                var i = 0;
                while (!stop.IsCancellationRequested)
                {
                    src.PushSkeleton(1);
                    if (images)
                    {
                        src.PushImages(1, 8, 4);
                    }

                    i++;
                    Thread.Sleep(10);
                }
            })
            { IsBackground = true };
            t.Start();
            return t;
        }

        [Fact]
        public void SkeletonRunWritesFileWithFrames()
        {
            var src = new FakePoseSource(Sensor());
            using var cts = new CancellationTokenSource();
            var t = Pump(src, false, cts.Token);
            var r = CaptureSession.Run(src, src, Options(dir, false));
            cts.Cancel();
            t.Join();
            Assert.Equal(CaptureExitCode.Ok, r.Code);
            Assert.InRange(r.SkeletonFrames, 20, 200);
            using var reader = new SkeletonRecordReader(File.OpenRead(Path.Combine(dir, "skeleton.jsonl")));
            Assert.Equal("test", reader.Header.Scenario);
            Assert.Equal("t1", reader.Header.TrackerVersion);
            Assert.Equal(r.SkeletonFrames, reader.ReadFrames().Count());
            Assert.False(File.Exists(Path.Combine(dir, "depth.bin")));
        }

        [Fact]
        public void StopRequestEndsRecordingEarlyAndKeepsFrames()
        {
            var src = new FakePoseSource(Sensor());
            using var cts = new CancellationTokenSource();
            var t = Pump(src, false, cts.Token);
            var started = DateTime.UtcNow;
            var o = Options(dir, false);
            o.DurationSeconds = 30;
            o.StopRequested = () => (DateTime.UtcNow - started).TotalSeconds > 1.0;
            var r = CaptureSession.Run(src, src, o);
            cts.Cancel();
            t.Join();
            Assert.Equal(CaptureExitCode.Ok, r.Code);
            Assert.True((DateTime.UtcNow - started).TotalSeconds < 10);
            Assert.InRange(r.SkeletonFrames, 10, 400);
            using var reader = new SkeletonRecordReader(File.OpenRead(Path.Combine(dir, "skeleton.jsonl")));
            Assert.Equal(r.SkeletonFrames, reader.ReadFrames().Count());
        }

        [Fact]
        public void ProgressLinesReportRemainingTime()
        {
            var src = new FakePoseSource(Sensor());
            using var cts = new CancellationTokenSource();
            var t = Pump(src, false, cts.Token);
            var lines = new System.Collections.Generic.List<string>();
            var o = Options(dir, false);
            o.DurationSeconds = 4.2;
            o.ProgressIntervalSeconds = 1;
            o.Log = lines.Add;
            CaptureSession.Run(src, src, o);
            cts.Cancel();
            t.Join();
            var progress = lines.Where(l => l.StartsWith("осталось ")).ToList();
            Assert.InRange(progress.Count, 2, 3);
            Assert.Equal("осталось 3 с до конца", progress[0]);
            Assert.Equal("осталось 2 с до конца", progress[1]);
        }

        [Fact]
        public void ImageRunWritesDepthColorAndSkeleton()
        {
            var src = new FakePoseSource(Sensor());
            using var cts = new CancellationTokenSource();
            var t = Pump(src, true, cts.Token);
            var r = CaptureSession.Run(src, src, Options(dir, true));
            cts.Cancel();
            t.Join();
            Assert.Equal(CaptureExitCode.Ok, r.Code);
            Assert.InRange(r.DepthFrames, 20, 200);
            Assert.Equal(r.DepthFrames * 8 * 4 * 2, new FileInfo(Path.Combine(dir, "depth.bin")).Length);
            Assert.Equal(r.ColorFrames * 8 * 4 * 4, new FileInfo(Path.Combine(dir, "color.bin")).Length);
            Assert.True(File.Exists(Path.Combine(dir, "run.json")));
            Assert.True(File.Exists(Path.Combine(dir, "skeleton.jsonl")));
        }

        [Fact]
        public void LowMemoryRefusesToStartAndWritesNoRecording()
        {
            var src = new FakePoseSource(Sensor());
            var r = CaptureSession.Run(src, src, Options(dir, true, () => 100));
            Assert.Equal(CaptureExitCode.PreflightFailed, r.Code);
            Assert.Contains("освободите", r.Message);
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public void MissingSensorReturnsNoSensor()
        {
            var src = new FakePoseSource(Sensor()) { IsAvailable = false };
            var r = CaptureSession.Run(src, src, Options(dir, false));
            Assert.Equal(CaptureExitCode.NoSensor, r.Code);
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public void RealisticSizesSmokeIfEnabled()
        {
            if (Environment.GetEnvironmentVariable("DANCELAB_HEAVY") != "1")
            {
                return;
            }

            var sensor = new SensorInfo("s1", "SN", new FrameSize(512, 424, 30, "Depth16"), new FrameSize(1920, 1080, 30, "Yuy2"));
            var src = new FakePoseSource(sensor);
            using var cts = new CancellationTokenSource();
            var t = new Thread(() =>
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                {
                    src.PushSkeleton(1);
                    src.PushImagesOfSize(i++, 512, 424, 1920, 1080);
                    Thread.Sleep(33);
                }
            })
            { IsBackground = true };
            t.Start();
            var o = Options(dir, true);
            o.DurationSeconds = 10;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = CaptureSession.Run(src, src, o);
            cts.Cancel();
            t.Join();
            Assert.Equal(CaptureExitCode.Ok, r.Code);
            Assert.InRange(r.DepthFrames, 100, 320);
            Assert.Equal(r.ColorFrames * 1920L * 1080 * 2, new FileInfo(Path.Combine(dir, "color.bin")).Length);
            Assert.True(sw.Elapsed.TotalSeconds < 60, "сохранение слишком долгое: " + sw.Elapsed);
        }
    }
}
